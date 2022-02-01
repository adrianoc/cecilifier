using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

partial class ExpressionVisitor
{
    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        /*
            // S *s = stackalloc S[n];
            IL_0007: ldarg.1
            IL_0008: conv.u
            IL_0009: sizeof MyStruct
            IL_000f: mul.ovf.un
            IL_0010: localloc
            
            // int *i = stackalloc int[10];
            IL_0001: ldc.i4.s 40
            IL_0003: conv.u
            IL_0004: localloc
         */
        var arrayType = (ArrayTypeSyntax) node.Type;
        var rankNode = arrayType.RankSpecifiers[0].Sizes[0];
        var arrayElementType = Context.SemanticModel.GetTypeInfo(arrayType.ElementType);
        
        Debug.Assert(arrayType.RankSpecifiers.Count == 1);
        if (rankNode.IsKind(SyntaxKind.OmittedArraySizeExpression))
        {
            Utils.EnsureNotNull(node.Initializer);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);
        }

        var stackallocSpanAssignmentTracker = new StackallocSpanAssignmentTracker(node, Context);
        uint sizeInBytes = 0;

        var arrayElementTypeSize = arrayElementType.Type.IsPrimitiveType()
            ? arrayElementType.Type.SizeofArrayLikeItemElement()
            : uint.MaxValue; // this means the size of the elements need to be calculated at runtime... 
        
        if (rankNode.IsKind(SyntaxKind.NumericLiteralExpression) && arrayElementType.Type.IsPrimitiveType())
        {
            sizeInBytes = (uint) Int32.Parse(rankNode.GetFirstToken().Text) * arrayElementTypeSize;
            stackallocSpanAssignmentTracker.RememberConstantAllocationLength(sizeInBytes);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, sizeInBytes);
            Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);
            Context.EmitCilInstruction(ilVar, OpCodes.Localloc);
        }
        else
        {
            rankNode.Accept(this);
            if (stackallocSpanAssignmentTracker.AddVariableToStoreAllocatedBytesLengthIfRequired(Context, rankNode))
            {
                // result of the stackalloc is being assigned to a Span<T>. We need to initialize it later.. for that we need
                // the length (in bytes) of the allocated memory. So we store in a new local variable.
                Context.EmitCilInstruction(ilVar, OpCodes.Stloc, stackallocSpanAssignmentTracker.SpanLengthVariable);
                Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, stackallocSpanAssignmentTracker.SpanLengthVariable);
            }
            
            Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);
            if (arrayElementTypeSize > 1) // Optimization
            {
                string operand = ResolveType(arrayType.ElementType);
                Context.EmitCilInstruction(ilVar, OpCodes.Sizeof, operand);
                Context.EmitCilInstruction(ilVar, OpCodes.Mul_Ovf_Un);
            }
            Context.EmitCilInstruction(ilVar, OpCodes.Localloc);
        }

        if (node.Initializer != null)
            node.Initializer.Accept(this);
        
        if (stackallocSpanAssignmentTracker)
        {
            if (stackallocSpanAssignmentTracker.IsConstantAllocationLength)
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, stackallocSpanAssignmentTracker.ConstantAllocationSizeInBytes);
            else
                Context.EmitCilInstruction(ilVar, stackallocSpanAssignmentTracker.LoadOpCode, stackallocSpanAssignmentTracker.SpanLengthVariable);

            var spanInstanceType = $"{Utils.ImportFromMainModule("typeof(Span<>)")}.MakeGenericInstanceType({ResolveType(arrayType.ElementType)})";
            var spanCtorVar = Context.Naming.SyntheticVariable("spanCtor", ElementKind.LocalVariable);
            AddCecilExpression($"var {spanCtorVar} = new MethodReference(\".ctor\", {Context.TypeResolver.Bcl.System.Void}, {spanInstanceType}) {{ HasThis = true }};");
            AddCecilExpression($"{spanCtorVar}.Parameters.Add({CecilDefinitionsFactory.Parameter("ptr", RefKind.None, Context.TypeResolver.Resolve("void*") )});");
            AddCecilExpression($"{spanCtorVar}.Parameters.Add({CecilDefinitionsFactory.Parameter("length", RefKind.None, Context.TypeResolver.Bcl.System.Int32)});");

            string operand = Utils.ImportFromMainModule($"{spanCtorVar}");
            Context.EmitCilInstruction(ilVar, OpCodes.Newobj, operand);
        }
    }
}

/*
 * `stackalloc` requires that the only element present in the stack to be
 * the size (in bytes) of the block bytes will be allocated. This disrupts
 * the 'normal invocation flow' in which stack looks like:
 *      [target of the call, arg1, ..., argn]
 *
 * If one (or more arguments) are stackalloc expressions then at the time
 * Cecilifier visits these expression it will already have visited (and
 * pushed the reference to the stack) of the target of the call and/or
 * any arguments that proceeds the stackaloc one. For instance, in the 
 * following call:
 *
 *   o.M(42, stackalloc byte[3]);
 *
 * when Cecilifier visits the stackalloc, the stack will not be empty; it
 * will look like:
 * 
 *      [o, 42]
 * 
 * i.e, the target of the call and the first argument.
 *
 * In order generate valid code Cecilifier detects that the call contains a stackalloc being passed
 * as an argument and after computing each argument introduces a local variable to store the computed value.
 *
 * After visiting the whole expression it ensures that these local variables are pushed to the stack
 * to recreate the stack frame for the call.
 */
internal interface IStackallocAsArgumentFixer
{
    void StoreTopOfStackToLocalVariable(ITypeSymbol type);
    void MarkEndOfComputedCallTargetBlock();
    void RestoreCallStackIfRequired();
    
    IVisitorContext Context { get; }
    IDisposable FlagAsHavingStackallocArguments();
}

class NoOpStackallocAsArgumentHandler : IStackallocAsArgumentFixer
{
    public NoOpStackallocAsArgumentHandler(IVisitorContext context) => Context = context;

    void IStackallocAsArgumentFixer.StoreTopOfStackToLocalVariable(ITypeSymbol type) { }

    void IStackallocAsArgumentFixer.MarkEndOfComputedCallTargetBlock() { }

    void IStackallocAsArgumentFixer.RestoreCallStackIfRequired()  { }
    public IVisitorContext Context { get; }
    public IDisposable FlagAsHavingStackallocArguments() => default;
}

internal class StackallocAsArgumentFixer : IStackallocAsArgumentFixer
{
    private static Stack<IStackallocAsArgumentFixer> handlers = new();
    private Queue<string> localVariablesStoringOriginalArguments = new();
    
    private IVisitorContext context;
    private readonly string ilVar;
    private LinkedListNode<string> lastLoadTargetOfCallInstruction;
    private LinkedListNode<string> firstLoadTargetOfCallInstruction;

    private StackallocAsArgumentFixer(IVisitorContext context, string ilVar)
    {
        firstLoadTargetOfCallInstruction = context.CurrentLine;
        this.context = context;
        this.ilVar = ilVar;
    }

    IVisitorContext IStackallocAsArgumentFixer.Context => context;
    IDisposable IStackallocAsArgumentFixer.FlagAsHavingStackallocArguments() => context.WithFlag(Constants.ContextFlags.HasStackallocArguments);

    void IStackallocAsArgumentFixer.StoreTopOfStackToLocalVariable(ITypeSymbol type)
    {
        if (!context.HasFlag(Constants.ContextFlags.HasStackallocArguments))
            return;
    
        var methodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        if (!methodVar.IsValid)
            throw new InvalidOperationException();
    
        var localVarName = type.Name;
        var cecilVarDeclName = context.Naming.SyntheticVariable(localVarName, ElementKind.LocalVariable);
    
        context.WriteCecilExpression($"var {cecilVarDeclName} = new VariableDefinition({context.TypeResolver.Resolve(type)});");
        context.WriteNewLine();
        context.WriteCecilExpression($"{methodVar.VariableName}.Body.Variables.Add({cecilVarDeclName});");
        context.WriteNewLine();

        context.DefinitionVariables.RegisterNonMethod(string.Empty, localVarName, VariableMemberKind.LocalVariable, cecilVarDeclName);
    
        context.WriteCecilExpression($"{ilVar}.Emit(OpCodes.Stloc, {cecilVarDeclName});");
        context.WriteNewLine();
    
        localVariablesStoringOriginalArguments.Enqueue(cecilVarDeclName);
    }

    void IStackallocAsArgumentFixer.MarkEndOfComputedCallTargetBlock()
    {
        lastLoadTargetOfCallInstruction = context.CurrentLine.Previous;
    }
    
    /// <summary>
    /// Restore the stack to: [call target, arg1, ... arg n]
    /// </summary>
    void IStackallocAsArgumentFixer.RestoreCallStackIfRequired()
    {
        if (!context.HasFlag(Constants.ContextFlags.HasStackallocArguments))
            return;

        // Current line at this point is the fixed call instruction  (see ExpressionVisitor.HandleMethodInvocation() to learn more about the `fixing` part)
        var callInstruction = context.CurrentLine;
        

        // Move all instructions in charge of loading the target (object reference) of the call just after 
        // the `call` instruction which will be fixed later. 
        var c = lastLoadTargetOfCallInstruction;
        while (c != firstLoadTargetOfCallInstruction)
        {
            var previous = c.Previous;
            context.MoveLineAfter(c, callInstruction);
            c = previous;
        }
        
        // emit instruction to push original arguments to stack
        foreach (var localVariable in localVariablesStoringOriginalArguments)
        {
            context.WriteCecilExpression($"{ilVar}.Emit(OpCodes.Ldloc, {localVariable});");
            context.WriteNewLine();
        }
        
        // now move the call instruction after the last argument
        context.MoveLineAfter(callInstruction, context.CurrentLine);
    }
    
    internal static StackallocPassedAsSpanDisposal TrackPassingStackAllocToSpanArgument(IVisitorContext context, InvocationExpressionSyntax node, string ilVar)
    {
        // the expression may represent: i) a method invocation, ii) a delegate invocation or iii) nameof() expression.
        // for the last 2 cases, `method` will be `null` and code will early out. 
        var method = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol as IMethodSymbol;
      
        // in this scenario, when stackalloc in processed, the stack will be empty (because there are
        // neither other parameters nor the target of the call to be pushed).
        if (method == null || (method.IsStatic && method.Parameters.Length < 2))
            return new StackallocPassedAsSpanDisposal();

        var isPassingStackAllocToSpanArg = node.ArgumentList.Arguments
            .Zip(method.Parameters)
            .Any(candidate => candidate.First.Expression.IsKind(SyntaxKind.StackAllocArrayCreationExpression)
                              && candidate.Second.Type.OriginalDefinition.MetadataToken == context.RoslynTypeSystem.SystemSpan.MetadataToken);

        return new StackallocPassedAsSpanDisposal(isPassingStackAllocToSpanArg ? new StackallocAsArgumentFixer(context, ilVar) : new NoOpStackallocAsArgumentHandler(context));
    }
    
    public static IStackallocAsArgumentFixer Current => handlers.Count > 0 ? handlers.Peek() : null;
    
    internal struct StackallocPassedAsSpanDisposal : IDisposable
    {
        private readonly IStackallocAsArgumentFixer parent;
        private IDisposable stackAllocFlagCleaner;

        public StackallocPassedAsSpanDisposal(IStackallocAsArgumentFixer parent = null)
        {
            this.parent = parent;
            stackAllocFlagCleaner = null;
            
            if (parent != null)
            {
                stackAllocFlagCleaner = parent.FlagAsHavingStackallocArguments();
                handlers.Push(parent);
            }
        }

        public void Dispose()
        {
            if (parent == null)
                return;

            parent.RestoreCallStackIfRequired();
            handlers.Pop();
            stackAllocFlagCleaner?.Dispose();
        }
    }
}

/*
 * Ensures that the correct code is generated if the result of the stackalloc
 * is being assigned (or passed as an argument) to a Span<> typed location. 
 */
internal class StackallocSpanAssignmentTracker
{
    private readonly bool isAssignedToSpan;
    private uint _sizeInBytes = UInt32.MaxValue;
    
    public StackallocSpanAssignmentTracker(StackAllocArrayCreationExpressionSyntax node, IVisitorContext context)
    {
        isAssignedToSpan = node.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault(vd => vd.Type.ToFullString().Contains("Span")) != null
                           || node.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault(vd => context.GetTypeInfo(vd.Expression).Type?.Name.Contains("Span") == true) != null;
    }
    
    public void RememberConstantAllocationLength(uint sizeInBytes)
    {
        _sizeInBytes = sizeInBytes;
    }

    public bool AddVariableToStoreAllocatedBytesLengthIfRequired(IVisitorContext context, ExpressionSyntax rankNode)
    {
        if (!this)
            return false;
        
        if (rankNode.IsKind(SyntaxKind.IdentifierName))
        {
            var rankSymbolInfo = context.SemanticModel.GetSymbolInfo(rankNode);
            Utils.EnsureNotNull(rankSymbolInfo.Symbol, "Failed to resolve symbol");

            var parentTypeName = rankSymbolInfo.Symbol.Kind == SymbolKind.Field // TODO: What about properties and/or methods? 
                ? rankSymbolInfo.Symbol.ContainingType.FullyQualifiedName()
                : string.Empty;
            
            var spanSizeStorageVariable = context.DefinitionVariables.GetVariable(rankNode.ToFullString(), VariableMemberKind.LocalVariable | VariableMemberKind.Field | VariableMemberKind.Parameter, parentTypeName);
            Debug.Assert(spanSizeStorageVariable.IsValid);
            SpanLengthVariable = spanSizeStorageVariable.VariableName;

            LoadOpCode = spanSizeStorageVariable.Kind switch
            {
                VariableMemberKind.Field => OpCodes.Ldfld,
                VariableMemberKind.Parameter => OpCodes.Ldarg,
                VariableMemberKind.LocalVariable => OpCodes.Ldloc,
                _ => throw new NotImplementedException($"stackalloc for {spanSizeStorageVariable.Kind} types are not supported."),
            };
            
            return false;
        }
        
        SpanLengthVariable = context.Naming.SyntheticVariable("spanSizeInBytes", ElementKind.LocalVariable);

        var currentMethodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        Debug.Assert(currentMethodVar.IsValid);

        context.WriteCecilExpression($"var {SpanLengthVariable} = new VariableDefinition({context.TypeResolver.Bcl.System.Int32});");
        context.WriteNewLine();
        context.WriteCecilExpression($"{currentMethodVar.VariableName}.Body.Variables.Add({SpanLengthVariable});");
        context.WriteNewLine();

        LoadOpCode = OpCodes.Ldloc;

        return true;;
    }

    public bool IsConstantAllocationLength => _sizeInBytes != UInt32.MaxValue;
    public uint ConstantAllocationSizeInBytes => _sizeInBytes;
    public string SpanLengthVariable { get; private set; }
    public OpCode LoadOpCode { get; private set; }

    public static implicit operator bool(in StackallocSpanAssignmentTracker obj) => obj.isAssignedToSpan;
}
