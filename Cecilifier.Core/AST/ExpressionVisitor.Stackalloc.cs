using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;
using static Cecilifier.Core.Misc.CodeGenerationHelpers;

namespace Cecilifier.Core.AST;

partial class ExpressionVisitor
{
    public override void VisitImplicitStackAllocArrayCreationExpression(ImplicitStackAllocArrayCreationExpressionSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        var spanType = Context.SemanticModel.GetTypeInfo(node).Type.EnsureNotNull<ITypeSymbol, INamedTypeSymbol>();
        var arrayElementType = spanType.TypeArguments[0];

        Utils.EnsureNotNull(node.Initializer);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);

        var stackallocSpanAssignmentTracker = new StackallocSpanAssignmentTracker(node, Context);
        var resolvedArrayElementType = Context.TypeResolver.Resolve(arrayElementType);
        CalculateLengthInBytesAndEmitLocalloc(stackallocSpanAssignmentTracker, null, resolvedArrayElementType, false);

        ProcessStackAllocInitializer(node.Initializer);

        if (!stackallocSpanAssignmentTracker)
            return;

        Context.EmitCilInstruction(ilVar, stackallocSpanAssignmentTracker.LoadOpCode, stackallocSpanAssignmentTracker.SpanLengthVariable);
        EmitNewobjForSpanOfType(resolvedArrayElementType);
    }

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

        var resolvedElementType = ResolveType(arrayType.ElementType);
        if (rankNode.IsKind(SyntaxKind.NumericLiteralExpression) && arrayElementType.Type.IsPrimitiveType())
        {
            var elementCount = Int32.Parse(rankNode.GetFirstToken().Text);
            sizeInBytes = (uint) elementCount * arrayElementTypeSize;
            stackallocSpanAssignmentTracker.RememberConstantElementCount(elementCount);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, sizeInBytes, $"{elementCount} (elements) * {arrayElementTypeSize} (bytes per element)");
            Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);
            Context.EmitCilInstruction(ilVar, OpCodes.Localloc);
        }
        else
        {
            rankNode.Accept(this);
            CalculateLengthInBytesAndEmitLocalloc(stackallocSpanAssignmentTracker, rankNode, resolvedElementType, arrayElementTypeSize > 1);
        }

        ProcessStackAllocInitializer(node.Initializer);

        if (stackallocSpanAssignmentTracker)
        {
            if (stackallocSpanAssignmentTracker.HasConstantElementCount)
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, stackallocSpanAssignmentTracker.ConstantElementCount);
            else
                Context.EmitCilInstruction(ilVar, stackallocSpanAssignmentTracker.LoadOpCode, stackallocSpanAssignmentTracker.SpanLengthVariable);
            EmitNewobjForSpanOfType(resolvedElementType);
        }
    }

    void ProcessStackAllocInitializer(InitializerExpressionSyntax node)
    {
        if (node == null)
            return;

        using var _ = LineInformationTracker.Track(Context, node);

        var typeInfo = Context.SemanticModel.GetTypeInfo(node.Parent!);
        if (typeInfo.ConvertedType == null)
            return;

        if (PrivateImplementationDetailsGenerator.IsApplicableTo(node, Context))
            EmitOptimizedInitialization(node, typeInfo);
        else
            EmitSlowInitialization(node, typeInfo);
    }

    private void EmitOptimizedInitialization(InitializerExpressionSyntax node, TypeInfo typeInfo)
    {
        var arrayType = (typeInfo.Type ?? typeInfo.ConvertedType).ElementTypeSymbolOf();
        var sizeInBytes = arrayType.SizeofArrayLikeItemElement() * node.Expressions.Count;
        var fieldWithRawData = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(Context, sizeInBytes, arrayType.Name, $"stackalloc {arrayType.Name}[] {node.ToFullString()}");
        
        Context.WriteNewLine();
        Context.WriteComment($"duplicates the top of the stack (the newly `stackalloced` buffer) and initialize it from the raw buffer ({fieldWithRawData}).");
        Context.EmitCilInstruction(ilVar, OpCodes.Dup);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldsflda, fieldWithRawData);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, sizeInBytes);
        Context.EmitCilInstruction(ilVar, OpCodes.Cpblk);
        Context.WriteComment("finished initializing `stackalloced` buffer.");
        Context.WriteNewLine();
    }

    private void EmitSlowInitialization(InitializerExpressionSyntax node, TypeInfo typeInfo)
    {
        var arrayType = typeInfo.Type ?? typeInfo.ConvertedType;
        uint elementTypeSize = arrayType.SizeofArrayLikeItemElement();
        uint offset = 0;

        foreach (var exp in node.Expressions)
        {
            Context.EmitCilInstruction(ilVar, OpCodes.Dup);
            if (offset != 0)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, offset);
                Context.EmitCilInstruction(ilVar, OpCodes.Add);
            }

            exp.Accept(this);
            Context.EmitCilInstruction(ilVar, arrayType.StindOpCodeFor());
            offset += elementTypeSize;
        }
    }

    private void CalculateLengthInBytesAndEmitLocalloc(StackallocSpanAssignmentTracker stackallocSpanAssignmentTracker, ExpressionSyntax rankNode, string resolvedElementType, bool elementSizeTakesMoreThanOneByte)
    {
        if (stackallocSpanAssignmentTracker.AddVariableToStoreElementCountIfRequired(Context, rankNode))
        {
            // result of the stackalloc is being assigned to a Span<T>. We need to initialize it later.. for that we need
            // element count so we store in a new local variable.
            Context.EmitCilInstruction(ilVar, OpCodes.Stloc, stackallocSpanAssignmentTracker.SpanLengthVariable);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, stackallocSpanAssignmentTracker.SpanLengthVariable);
        }

        Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);
        if (elementSizeTakesMoreThanOneByte) // if element type takes one byte them 'number of elements' == 'size in bytes' and we don't need to multiply
        {
            Context.EmitCilInstruction(ilVar, OpCodes.Sizeof, resolvedElementType);
            Context.EmitCilInstruction(ilVar, OpCodes.Mul_Ovf_Un);
        }

        Context.EmitCilInstruction(ilVar, OpCodes.Localloc);
    }

    private void EmitNewobjForSpanOfType(string resolvedSpanType)
    {
        var spanInstanceType = Context.TypeResolver.Resolve(Context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(resolvedSpanType);
        var spanCtorVar = Context.Naming.SyntheticVariable("spanCtor", ElementKind.LocalVariable);
        AddCecilExpression($"var {spanCtorVar} = new MethodReference(\".ctor\", {Context.TypeResolver.Bcl.System.Void}, {spanInstanceType}) {{ HasThis = true }};");
        AddCecilExpression($"{spanCtorVar}.Parameters.Add({CecilDefinitionsFactory.Parameter("ptr", RefKind.None, Context.TypeResolver.Resolve("void*"))});");
        AddCecilExpression($"{spanCtorVar}.Parameters.Add({CecilDefinitionsFactory.Parameter("length", RefKind.None, Context.TypeResolver.Bcl.System.Int32)});");

        Context.EmitCilInstruction(ilVar, OpCodes.Newobj, Utils.ImportFromMainModule($"{spanCtorVar}"));
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
 * any arguments that precedes the stackalloc one. For instance, in the 
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
 * as an argument and after processing it it introduces a local variable to store the computed value.
 *
 * After visiting the whole expression it ensures that these local variables are pushed to the stack
 * to recreate the stack frame for the call.
 */
internal interface IStackallocAsArgumentFixer
{
    void StoreTopOfStackToLocalVariable(ITypeSymbol type);
    void MarkEndOfComputedCallTargetBlock(LinkedListNode<string> last);
    void RestoreCallStackIfRequired();

    IDisposable FlagAsHavingStackallocArguments();
}

class NoOpStackallocAsArgumentHandler : IStackallocAsArgumentFixer
{
    public NoOpStackallocAsArgumentHandler(IVisitorContext context) => Context = context;

    void IStackallocAsArgumentFixer.StoreTopOfStackToLocalVariable(ITypeSymbol type) { }

    void IStackallocAsArgumentFixer.MarkEndOfComputedCallTargetBlock(LinkedListNode<string> last) { }

    void IStackallocAsArgumentFixer.RestoreCallStackIfRequired() { }
    public IVisitorContext Context { get; }
    public IDisposable FlagAsHavingStackallocArguments() => default;
}

internal class StackallocAsArgumentFixer : IStackallocAsArgumentFixer
{
    private static readonly Stack<IStackallocAsArgumentFixer> handlers = new();
    private readonly Queue<string> localVariablesStoringOriginalArguments = new();

    private readonly IVisitorContext context;
    private readonly string ilVar;
    private LinkedListNode<string> lastLoadTargetOfCallInstruction;
    private LinkedListNode<string> firstLoadTargetOfCallInstruction;

    private StackallocAsArgumentFixer(IVisitorContext context, string ilVar)
    {
        firstLoadTargetOfCallInstruction = context.CurrentLine;
        this.context = context;
        this.ilVar = ilVar;
    }

    IDisposable IStackallocAsArgumentFixer.FlagAsHavingStackallocArguments() => context.WithFlag<ContextFlagReseter>(Constants.ContextFlags.HasStackallocArguments);

    void IStackallocAsArgumentFixer.StoreTopOfStackToLocalVariable(ITypeSymbol type)
    {
        if (!context.HasFlag(Constants.ContextFlags.HasStackallocArguments))
            return;

        var methodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        if (!methodVar.IsValid)
            throw new InvalidOperationException();

        var cecilVarDeclName = StoreTopOfStackInLocalVariable(context, ilVar, type.Name, type).VariableName;
        context.WriteNewLine();

        localVariablesStoringOriginalArguments.Enqueue(cecilVarDeclName);
    }

    void IStackallocAsArgumentFixer.MarkEndOfComputedCallTargetBlock(LinkedListNode<string> last) => lastLoadTargetOfCallInstruction = last;

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
            context.EmitCilInstruction(ilVar, OpCodes.Ldloc, localVariable);
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
        private IStackallocAsArgumentFixer parent;
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
            var p = Interlocked.Exchange(ref parent, null);
            if (p == null)
                return;

            p.RestoreCallStackIfRequired();
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
    private int _constElementCount = 0;

    public StackallocSpanAssignmentTracker(SyntaxNode node, IVisitorContext context)
    {
        isAssignedToSpan = node.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault(vd => vd.Type.ToFullString().Contains("Span")) != null
                           || node.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault(vd => context.GetTypeInfo(vd.Expression).Type?.Name.Contains("Span") == true) != null;
    }

    public void RememberConstantElementCount(int elementCount)
    {
        _constElementCount = elementCount;
    }

    public bool AddVariableToStoreElementCountIfRequired(IVisitorContext context, ExpressionSyntax rankNode)
    {
        if (!isAssignedToSpan)
            return false;

        if (rankNode != null && rankNode.IsKind(SyntaxKind.IdentifierName))
        {
            var rankSymbolInfo = context.SemanticModel.GetSymbolInfo(rankNode);
            Utils.EnsureNotNull(rankSymbolInfo.Symbol, "Failed to resolve symbol");

            var parentTypeName = rankSymbolInfo.Symbol.Kind == SymbolKind.Field // TODO: What about properties and/or methods? 
                ? rankSymbolInfo.Symbol.ContainingType.ToDisplayString()
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

        SpanLengthVariable = context.Naming.SyntheticVariable("spanElementCount", ElementKind.LocalVariable);

        var currentMethodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        Debug.Assert(currentMethodVar.IsValid);

        context.WriteCecilExpression($"var {SpanLengthVariable} = new VariableDefinition({context.TypeResolver.Bcl.System.Int32});");
        context.WriteNewLine();
        context.WriteCecilExpression($"{currentMethodVar.VariableName}.Body.Variables.Add({SpanLengthVariable});");
        context.WriteNewLine();

        LoadOpCode = OpCodes.Ldloc;

        return true;
    }

    public bool HasConstantElementCount => _constElementCount != 0;
    public int ConstantElementCount => _constElementCount;
    public string SpanLengthVariable { get; private set; }
    public OpCode LoadOpCode { get; private set; }

    public static implicit operator bool(in StackallocSpanAssignmentTracker obj) => obj.isAssignedToSpan;
}
