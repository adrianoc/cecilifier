using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Handles;
using Cecilifier.Core.AST.Params;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.CodeGeneration.Extensions;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

using static Cecilifier.Core.Misc.CodeGenerationHelpers;

#nullable enable
namespace Cecilifier.Core.AST
{
    internal partial class ExpressionVisitor : SyntaxWalkerBase
    {
        private static readonly Dictionary<SyntaxKind, BinaryOperatorHandler> operatorHandlers = new();

        private readonly string ilVar;
        private readonly Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();

        // if true, while visiting an AssignmentExpression its left side must not be visited.
        // this is used, for example, in value type ctor invocation in which case there's
        // no value in the stack to be stored after the ctor is run
        private bool skipLeftSideVisitingInAssignment;
        
        /// When processing method invocations keep track of the last instruction used to load the target of the
        /// invocation. For example, in the invocation 'M1(i+1).M2(42)' this will be set twice: i) one for the target
        /// of the call to M1() and ii) a second one for the target of the call to M2() generating the pseudo IL code:
        ///
        /// IL1: Ldarg_0 # loads the implicit 'this' reference used to call M1().
        /// IL2: Ldarg_1 # loads parameter 'i'
        /// IL3: Ldc_I4_1 # loads constant 1
        /// IL4: Add      # i + 1
        /// IL5: Call M1(int)
        /// IL6: Ldc_I4, 42 # loads constant 42
        /// IL7:  M2(int)
        ///  
        /// In this scenario, '_lastInstructionLoadingTargetOfInvocation' will point to IL1 when processing
        /// the call to M1() and to IL5 when processing the call to M2()
        private LinkedListNode<string>? _lastInstructionLoadingTargetOfInvocation;

        private ExpandedParamsArgumentHandler? _expandedParamsArgumentHandler;

        static ExpressionVisitor()
        {
            // Arithmetic operators
            operatorHandlers[SyntaxKind.PlusToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) =>
            {
                if (left.SpecialType == SpecialType.System_String)
                {
                    var concatArgType = right.SpecialType == SpecialType.System_String ? "string" : "object";
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Call, $"assembly.MainModule.Import(typeof(string).GetMethod(\"Concat\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] {{ typeof({concatArgType}), typeof({concatArgType}) }}, null))");
                }
                else
                {
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Add);
                }
            });

            operatorHandlers[SyntaxKind.MinusToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Sub));
            operatorHandlers[SyntaxKind.AsteriskToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Mul));
            operatorHandlers[SyntaxKind.SlashToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Div));
            operatorHandlers[SyntaxKind.PercentToken] = BinaryOperatorHandler.Raw(HandleModulusExpression);

            operatorHandlers[SyntaxKind.GreaterThanToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, CompareOpCodeFor(left)));
            operatorHandlers[SyntaxKind.GreaterThanEqualsToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) =>
            {
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Clt);
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ldc_I4_0);
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ceq);
            });

            operatorHandlers[SyntaxKind.LessThanEqualsToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) =>
            {
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Cgt);
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ldc_I4_0);
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ceq);
            });

            operatorHandlers[SyntaxKind.EqualsEqualsToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ceq));
            operatorHandlers[SyntaxKind.LessThanToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Clt));
            operatorHandlers[SyntaxKind.ExclamationEqualsToken] = BinaryOperatorHandler.Raw((ctx, ilVar, left, right) =>
            {
                // This is not the most optimized way to handle != operator but it is generic and correct.
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ceq);
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ldc_I4_0);
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ceq);
            });

            // Bitwise Operators
            operatorHandlers[SyntaxKind.AmpersandToken] = BinaryOperatorHandler.Raw((ctx, ilVar, _, _) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.And));
            operatorHandlers[SyntaxKind.BarToken] = BinaryOperatorHandler.Raw((ctx, ilVar, _, _) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Or));
            operatorHandlers[SyntaxKind.CaretToken] = BinaryOperatorHandler.Raw((ctx, ilVar, _, _) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Xor));
            operatorHandlers[SyntaxKind.LessThanLessThanToken] = BinaryOperatorHandler.Raw((ctx, ilVar, _, _) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Shl));
            operatorHandlers[SyntaxKind.GreaterThanGreaterThanToken] = BinaryOperatorHandler.Raw((ctx, ilVar, _, _) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Shr));

            // Logical Operators
            operatorHandlers[SyntaxKind.AmpersandAmpersandToken] = BinaryOperatorHandler.Raw((ctx, ilVar, _, _) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.And));
            operatorHandlers[SyntaxKind.BarBarToken] = BinaryOperatorHandler.Raw((ctx, ilVar, _, _) => ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Or));

            operatorHandlers[SyntaxKind.IsKeyword] = BinaryOperatorHandler.Raw((ctx, ilVar, _, rightType) =>
            {
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Isinst, ctx.TypeResolver.ResolveAny(rightType));
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ldnull);
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Cgt);
            }, visitRightOperand: false); // Isinst opcode takes the type to check as a parameter (instead of taking it from the stack) so
                                          // we must not visit the right hand side of the binary expression

            operatorHandlers[SyntaxKind.QuestionQuestionToken] = new BinaryOperatorHandler((ctx, ilVar, binaryExpression, expressionVisitor) =>
            {
                // Null coalescing operator `??`
                var lhsType = ctx.SemanticModel.GetTypeInfo(binaryExpression.Left).Type;
                if (SymbolEqualityComparer.Default.Equals(lhsType?.OriginalDefinition, ctx.RoslynTypeSystem.SystemNullableOfT))
                {
                    binaryExpression.Left.Accept(expressionVisitor);
                    var evaluatedLeftVar = ctx.AddLocalVariableToCurrentMethod(
                        "leftValue", 
                        ctx.TypeResolver.ResolveAny(lhsType));
                    
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Stloc, new CilLocalVariableHandle(evaluatedLeftVar.VariableName));
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ldloca_S, new CilLocalVariableHandle(evaluatedLeftVar.VariableName));
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Call, lhsType.GetMembers("get_HasValue").OfType<IMethodSymbol>().Single().MethodResolverExpression(ctx));
                    
                    var loadLeftValueInst = ctx.Naming.Instruction("loadLeftValueTarget");
                    //TODO: we can't generate like this. We need to call into the driver to generate the instruction
                    ctx.Generate($"var {loadLeftValueInst} = {ilVar}.Create({OpCodes.Ldloc_S.ConstantName()}, {evaluatedLeftVar.VariableName});");
                    ctx.WriteNewLine();

                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Brtrue_S, loadLeftValueInst);

                    binaryExpression.Right.Accept(expressionVisitor);
                    binaryExpression.Right.InjectRequiredConversions(ctx, ilVar);
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Ret);
                    ctx.Generate($"{ilVar}.Body.Instructions.Add({loadLeftValueInst});");
                    ctx.WriteNewLine();
                    // method handler will add the required ret
                }
                else
                {
                    var returnInstruction = ctx.Naming.Instruction("return");
                    ctx.Generate($"var {returnInstruction} = {ilVar}.Create({OpCodes.Nop.ConstantName()});");
                    ctx.WriteNewLine();

                    binaryExpression.Left.Accept(expressionVisitor);
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Dup);
                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Brtrue_S, returnInstruction);

                    ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Pop); // removes evaluated LEFT expression from stack
                    binaryExpression.Right.Accept(expressionVisitor);
                    binaryExpression.Right.InjectRequiredConversions(ctx, ilVar);
                    ctx.Generate($"{ilVar}.Body.Instructions.Add({returnInstruction});");
                    ctx.WriteNewLine();
                }
            });
        }
        
        private static OpCode CompareOpCodeFor(ITypeSymbol left)
        {
            switch (left.SpecialType)
            {
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Byte:
                    return OpCodes.Cgt_Un;

                case SpecialType.None:
                    return left.Kind == SymbolKind.PointerType ? OpCodes.Cgt_Un : OpCodes.Cgt;

                default:
                    return OpCodes.Cgt;
            }
        }

        private ExpressionVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
            this.ilVar = ilVar;
        }

        public string ILVariable => ilVar;

        internal static bool Visit(IVisitorContext ctx, string ilVar, SyntaxNode node)
        {
            if (node == null)
            {
                return true;
            }

            var ev = new ExpressionVisitor(ctx, ilVar);
            ev.Visit(node);
            
            return ev.skipLeftSideVisitingInAssignment;
        }

        internal static bool VisitAndPopIfNotConsumed(IVisitorContext ctx, string ilVar, ExpressionSyntax node)
        {
            var ret = Visit(ctx, ilVar, node);
            PopIfNotConsumed(ctx, ilVar, node);

            return ret;
        }
        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.Expression == null)
                return;
            node.Expression.Accept(this);
            node.Expression.InjectRequiredConversions(Context, ilVar);
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Context.WriteComment(node.Expression.ToString());
            node.Expression.Accept(this);
            node.Expression.InjectRequiredConversions(Context, ilVar);
        }

        public override void VisitInitializerExpression(InitializerExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.IsKind(SyntaxKind.ArrayInitializerExpression))
            {
                // handles array initialization in the form `int []x = {1, 2, 3};`
                // Notice that even though the documentation call this an 'implicitly typed array'
                // `ImplicitArrayCreationExpressionSyntax` is not a parent of the initializer expression, 
                // which means, VisitImplicitArrayCreationExpression() will not be called
                // https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/arrays/single-dimensional-arrays
                ProcessImplicitArrayCreationExpression(node, Context.GetTypeInfo(node).ConvertedType.EnsureNotNull<ITypeSymbol, ITypeSymbol>());
            }
            else if (node.IsKind(SyntaxKind.CollectionInitializerExpression))
            {
                foreach (var initializeExp in node.Expressions)
                {
                    // Collection initializers with this syntax depend on the type being initialized to expose an `Add()` method
                    // (or an extension method to be available) with a signature that matches the types passed in the list of
                    // expressions
                    var addMethod = Context.SemanticModel.GetCollectionInitializerSymbolInfo(node.Expressions.First()).Symbol.EnsureNotNull<ISymbol, IMethodSymbol>();
                    Context.EnsureForwardedMethod(addMethod);
                    
                    Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Dup);
                    initializeExp.Accept(this);
                    Context.AddCallToMethod(addMethod, ilVar, MethodDispatchInformation.MostLikelyVirtual);
                }
            }
            else
            {
                foreach (var initializeExp in node.Expressions)
                    initializeExp.Accept(this);
            }
        }

        public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.Initializer == null)
            {
                node.Type.RankSpecifiers[0].Accept(this);
            }
            else
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);
            }

            var arrayTypeSymbol = Context.GetTypeInfo(node.Type).Type.EnsureNotNull<ITypeSymbol, IArrayTypeSymbol>();
            ProcessArrayCreation(arrayTypeSymbol.ElementType, node.Initializer!);
        }

        public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            ProcessImplicitArrayCreationExpression(node.Initializer, Context.GetTypeInfo(node).Type!);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var expressionInfo = Context.SemanticModel.GetSymbolInfo(node.Expression);
            if (expressionInfo.Symbol == null)
                return;

            var targetType = expressionInfo.Symbol.Accept(ElementTypeSymbolResolver.Instance)!;
            if (SymbolEqualityComparer.Default.Equals(targetType.OriginalDefinition, Context.RoslynTypeSystem.SystemSpan)
                && node.ArgumentList.Arguments.Count == 1
                && SymbolEqualityComparer.Default.Equals(Context.GetTypeInfo(node.ArgumentList.Arguments[0].Expression).Type, Context.RoslynTypeSystem.SystemRange))
            {
                node.Accept(new ElementAccessExpressionWithRangeArgumentVisitor(Context, ilVar, this));
                return;
            }

            if (InlineArrayProcessor.TryHandleRangeElementAccess(Context, this, ilVar, node, out var elementType1))
            {
                return;
            }

            if (InlineArrayProcessor.TryHandleIntIndexElementAccess(Context, ilVar, node, out var elementType))
            {
                // if the parent of the element access expression is a member access expression the code 
                // that handles that expects that the target instance is at the top of the stack so; in 
                // the case of that target being an inline array element, that means that the address of
                // the entry should be at the top of the stack which is exactly how
                // TryHandleInlineArrayElementAccess() will leave the stack so in this case there's nothing.
                // else to be done ...
                if (node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {
                    if (UsageVisitor.GetInstance(Context).Visit(node.Parent) == UsageKind.CallTarget)
                    {
                        Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, Context.TypeResolver.ResolveAny(elementType));
                        return;
                    }
                    
                    if (elementType.TypeKind != TypeKind.TypeParameter)
                        return;
                }
                
                // ... otherwise, we need to take the top of the stack (address of the element) and load the actual instance to the stack.
                var loadOpCode = elementType.LdindOpCodeFor();
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, loadOpCode, loadOpCode == OpCodes.Ldobj ? Context.TypeResolver.ResolveAny(elementType) : null);
                return;
            }

            node.Expression.Accept(this);
            node.ArgumentList.Accept(this);

            var indexer = targetType.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(p => p.IsIndexer && p.Parameters.Length == node.ArgumentList.Arguments.Count);
            if (expressionInfo.Symbol.GetMemberType().Kind != SymbolKind.ArrayType && indexer != null)
            {
                indexer.EnsurePropertyExists(Context, node);
                Context.AddCallToMethod(indexer.GetMethod, ilVar, MethodDispatchInformation.MostLikelyVirtual);
                HandlePotentialRefLoad(ilVar, node, indexer.Type);
            }
            else if (node.Parent.IsKind(SyntaxKind.RefExpression))
            {
                AddCilInstruction(ilVar, OpCodes.Ldelema, targetType);
            }
            else
            {
                if (HandleLoadAddress(ilVar, targetType, node, OpCodes.Ldelema, Context.TypeResolver.ResolveAny(targetType)))
                    return;
                
                var ldelemOpCodeToUse = targetType.LdelemOpCode();
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, ldelemOpCodeToUse, ldelemOpCodeToUse == OpCodes.Ldelem ? Context.TypeResolver.ResolveAny(targetType) : null);
            }
        }

        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            base.VisitEqualsValueClause(node);
            node.Value.InjectRequiredConversions(Context, ilVar);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (HandlePseudoAssignment(node))
                return;

            var visitor = new AssignmentVisitor(Context, ilVar, node);

            visitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
            Visit(node.Right);
            node.Right.InjectRequiredConversions(Context, ilVar);
            if (!skipLeftSideVisitingInAssignment)
            {
                ProcessCompoundAssignmentExpression(node, visitor);
                visitor.Visit(node.Left);
            }

            ProcessEventAssignment(node);
        }

        private void ProcessEventAssignment(AssignmentExpressionSyntax node)
        {
            var leftNodeMae = node.Left as MemberAccessExpressionSyntax;
            CSharpSyntaxNode exp = leftNodeMae?.Name ?? node.Left;
            var expSymbol = Context.SemanticModel.GetSymbolInfo(exp).Symbol;
            if (expSymbol is not IEventSymbol @event)
                return;

            MethodDispatchInformation dispatchInformation = node.Left.MethodDispatchInformation();
            Context.AddCallToMethod(node.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken) ? @event.AddMethod : @event.RemoveMethod, ilVar, dispatchInformation);
        }

        private void ProcessCompoundAssignmentExpression(AssignmentExpressionSyntax node, AssignmentVisitor visitor)
        {
            var equivalentTokenKind = node.Kind().MapCompoundAssignment();
            if (equivalentTokenKind == SyntaxKind.None || Context.SemanticModel.GetSymbolInfo(node.Left).Symbol?.Kind == SymbolKind.Event)
                return;

            // x += y;
            var lastInstructionLoadingRightExpression = Context.CurrentLine;
            Visit(node.Left); // load `x`

            // for some types (for example those with overloaded operator+), the loading order of x (left) and y (right) may be important.
            // Since we visited right (y) then left (x), to preserve the logical order we need to move all lines related to loading right (y)
            // after the last instruction (which is the last instruction in charge of loading left, i.e, x)
            Context.MoveLinesToEnd(visitor.InstructionPrecedingValueToLoad, lastInstructionLoadingRightExpression);

            if (node.IsOperatorOnCustomUserType(Context.SemanticModel, out var method))
            {
                if (method.IsDefinedInCurrentAssembly(Context))
                    Context.EnsureForwardedMethod(method);
                Context.AddCallToMethod(method, ilVar, MethodDispatchInformation.MostLikelyVirtual);
            }
            else
                operatorHandlers[equivalentTokenKind].ProcessRaw(Context, ilVar, node.Left, node.Right);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.IsOperatorOnCustomUserType(Context.SemanticModel, out var method))
            {
                ProcessOverloadedBinaryOperatorInvocation(node, method);
            }
            else
            {
                ProcessBinaryExpression(node);
            }
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            var literalParent = (CSharpSyntaxNode) node.Parent!;
            Debug.Assert(literalParent != null);

            var nodeType = Context.SemanticModel.GetTypeInfo(node);
            var literalType = nodeType.Type ?? nodeType.ConvertedType;

            var value = node.Token.Kind() switch
            {
                SyntaxKind.NullKeyword => null,
                SyntaxKind.SingleLineRawStringLiteralToken => node.Token.ValueText,
                SyntaxKind.MultiLineRawStringLiteralToken => node.Token.ValueText, // ValueText does not includes quotes, so nothing to remove.
                SyntaxKind.StringLiteralToken => node.Token.Text.Substring(1, node.Token.Text.Length - 2), // removes quotes because LoadLiteralValue() expects string to not be quoted.
                SyntaxKind.CharacterLiteralToken => node.Token.Text.Substring(1, node.Token.Text.Length - 2), // removes quotes because LoadLiteralValue() expects chars to not be quoted.
                SyntaxKind.DefaultKeyword => literalType!.ValueForDefaultLiteral(),
                _ => node.Token.Text
            };

            literalType ??= Context.SemanticModel.GetTypeInfo(literalParent).Type!;
            LoadLiteralValue(ilVar,
                literalType,
                value,
                literalParent.Accept(UsageVisitor.GetInstance(Context)),
                literalParent);

            skipLeftSideVisitingInAssignment = (literalType.IsValueType || literalType.TypeKind == TypeKind.TypeParameter) && !literalType.IsPrimitiveType();
        }

        public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.Parent is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                var localSymbol = Context.SemanticModel.GetSymbolInfo(node).Symbol.EnsureNotNull<ISymbol, ILocalSymbol>();
                var designation = ((SingleVariableDesignationSyntax) node.Designation);
                var resolvedOutArgType = Context.TypeResolver.ResolveAny(localSymbol.Type);

                DefinitionVariable methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
                var outLocalName = Context.ApiDefinitionsFactory.LocalVariable(Context, designation.Identifier.Text, methodVar.VariableName, resolvedOutArgType).VariableName;

                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloca_S, outLocalName);
            }

            base.VisitDeclarationExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            using var __ = LineInformationTracker.Track(Context, node);
            using var _ = StackallocAsArgumentFixer.TrackPassingStackAllocToSpanArgument(Context, node, ilVar);
            var constantValue = Context.SemanticModel.GetConstantValue(node);
            if (constantValue.HasValue && node.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" })
            {
                string operand = $"\"{node.ArgumentList.Arguments[0].ToFullString()}\"";
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldstr, operand);
                return;
            }

            HandleMethodInvocation(node.Expression, node.ArgumentList, Context.SemanticModel.GetSymbolInfo(node.Expression).Symbol);
            StoreTopOfStackInLocalVariableAndReloadItIfNeeded(node);
        }

        public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            var whenTrueLabel = EmitTargetLabel("whenTrue");
            var conditionalEnd = EmitTargetLabel("conditionEnd");

            // load expression and check if it is null...
            node.Expression.Accept(this);

            var targetEvaluationDoNotHaveSideEffects = Context.SemanticModel.GetSymbolInfo(node.Expression).Symbol?.Kind is SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Field;
            if (!targetEvaluationDoNotHaveSideEffects)
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Dup);

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Brtrue_S, whenTrueLabel);

            if (!targetEvaluationDoNotHaveSideEffects)
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Pop);

            // code to handle null case 
            var currentMethodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            var expressionTypeInfo = Context.SemanticModel.GetTypeInfo(node);
            var resolvedConcreteNullableType = Context.TypeResolver.ResolveAny(expressionTypeInfo.Type);
            var tempNullableVar = Context.ApiDefinitionsFactory.LocalVariable(Context, "nullable", currentMethodVar.VariableName, resolvedConcreteNullableType).VariableName;

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloca_S, tempNullableVar);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Initobj, resolvedConcreteNullableType);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloc, tempNullableVar);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Br, conditionalEnd);

            // handle not null case...
            AddCecilExpression("{0}.Append({1});", ilVar, whenTrueLabel);
            if (targetEvaluationDoNotHaveSideEffects)
                node.Expression.Accept(this);

            node.WhenNotNull.Accept(this);

            var nullableCtor = expressionTypeInfo.Type?.GetMembers(".ctor")
                .OfType<IMethodSymbol>()
                .Single(method => method.Parameters.Length == 1 && method.Parameters[0].Type.MetadataToken == ((INamedTypeSymbol) expressionTypeInfo.Type).TypeArguments[0].MetadataToken);

            Context.ApiDriver.WriteCilInstruction(Context, ilVar,
                OpCodes.Newobj,
                nullableCtor.MethodResolverExpression(Context));

            AddCecilExpression("{0}.Append({1});", ilVar, conditionalEnd);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            var conditionEnd = EmitTargetLabel("conditionEnd");
            var whenFalse = EmitTargetLabel("whenFalse");

            Visit(node.Condition);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Brfalse_S, whenFalse);

            Visit(node.WhenTrue);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Br_S, conditionEnd);

            AddCecilExpression("{0}.Append({1});", ilVar, whenFalse);
            Visit(node.WhenFalse);

            AddCecilExpression("{0}.Append({1});", ilVar, conditionEnd);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            HandleIdentifier(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            HandleIdentifier(node);
        }

        public override void VisitArgumentList(ArgumentListSyntax node)
        {
            var candidateSymbol = Context.SemanticModel.GetSymbolInfo(node.Parent!).Symbol.EnsureNotNull();
            if (candidateSymbol is IMethodSymbol method)
            {
                _expandedParamsArgumentHandler = method.CreateExpandedParamsUsageHandler(this, ilVar, node);
            }
            base.VisitArgumentList(node);
            _expandedParamsArgumentHandler?.PostProcessArgumentList(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            // in the case the parent of the argument syntax is an array access
            // *CurrentLine* will represent the instruction that loaded the array
            // reference into the stack.
            //
            // If the argument is a System.Index the actual offset to be used
            // need to be calculated based on the length of the array (due to 
            // Index supporting the concept of *from the end*)
            //
            // In this case InjectRequiredConversions will call the Action
            // passed and we can add the same instruction to load 
            // the array again (the array reference is necessary to 
            // compute it's length)
            var last = Context.CurrentLine;

            _expandedParamsArgumentHandler?.PreProcessArgument(node);
            using var nah = new NullLiteralArgumentDecorator(Context, node, ilVar);
            base.VisitArgument(node);

            node.Expression.InjectRequiredConversions(Context, ilVar, () =>
            {
                AddCecilExpression(last.Value.Replace("\t", string.Empty).Replace("\n", String.Empty));
            });

            StackallocAsArgumentFixer.Current?.StoreTopOfStackToLocalVariable(Context.SemanticModel.GetTypeInfo(node.Expression).Type);
            _expandedParamsArgumentHandler?.PostProcessArgument(node);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.IsOperatorOnCustomUserType(Context.SemanticModel, out var method))
            {
                Visit(node.Operand);
                Context.AddCallToMethod(method, ilVar, MethodDispatchInformation.MostLikelyVirtual);
                return;
            }

            if (node.OperatorToken.IsKind(SyntaxKind.AmpersandToken))
            {
                Visit(node.Operand);
            }
            else if (node.IsKind(SyntaxKind.UnaryMinusExpression))
            {
                Visit(node.Operand);
                node.Operand.InjectRequiredConversions(Context, ilVar);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Neg);
            }
            else if (node.IsKind(SyntaxKind.PreDecrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Sub, true);
            }
            else if (node.IsKind(SyntaxKind.PreIncrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Add, true);
            }
            else if (node.IsKind(SyntaxKind.LogicalNotExpression))
            {
                node.Operand.Accept(this);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4_0);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ceq);
            }
            else if (node.IsKind(SyntaxKind.BitwiseNotExpression))
            {
                node.Operand.Accept(this);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Not);
            }
            else if (node.IsKind(SyntaxKind.IndexExpression))
            {
                ProcessIndexerExpression(node);
            }
            else
            {
                LogUnsupportedSyntax(node);
            }
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.IsOperatorOnCustomUserType(Context.SemanticModel, out var method))
            {
                Visit(node.Operand);
                Context.AddCallToMethod(method, ilVar, MethodDispatchInformation.MostLikelyVirtual);
                return;
            }

            if (node.IsKind(SyntaxKind.PostDecrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Sub, false);
            }
            else if (node.IsKind(SyntaxKind.PostIncrementExpression))
            {
                ProcessPrefixPostfixOperators(node.Operand, OpCodes.Add, false);
            }
            else
            {
                LogUnsupportedSyntax(node);
            }
        }

        public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            base.VisitParenthesizedExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            ProcessImplicitAndExplicitObjectCreationExpression(node);
        }
        
        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            ProcessImplicitAndExplicitObjectCreationExpression(node);
        }
        
        private void ProcessImplicitAndExplicitObjectCreationExpression(BaseObjectCreationExpressionSyntax node)
        {
            var ctorInfo = Context.SemanticModel.GetSymbolInfo(node);

            var ctor = (IMethodSymbol?) ctorInfo.Symbol;
            if (ctor == null)
            {
                if (TryProcessTypeParameterInstantiation(node))
                    return;

                if (!TryProcessMethodReferenceInObjectCreationExpression(node))
                    throw new InvalidOperationException($"Failed to resolve called constructor symbol in {node}");

                return;
            }

            if (TryProcessInvocationOnParameterlessImplicitCtorOnValueType(node, ctorInfo))
                return;

            Context.EnsureForwardedMethod(ctor);

            var operand = ctor.MethodResolverExpression(Context);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Newobj, operand.AsToken());
            PushCall();

            Visit(node.ArgumentList);
            FixCallSite();

            StoreTopOfStackInLocalVariableAndReloadItIfNeeded(node);

            node.Initializer?.Accept(this);
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            base.Visit(node.Expression);
            PopIfNotConsumed(Context, ilVar, node.Expression);
        }

        public override void VisitThisExpression(ThisExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
            base.VisitThisExpression(node);
        }

        public override void VisitRefExpression(RefExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            using (Context.WithFlag<ContextFlagReseter>(Constants.ContextFlags.RefReturn))
            {
                node.Expression.Accept(this);
            }
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            node.Expression.Accept(this);
            var castSourceType = Context.GetTypeInfo(node.Expression).Type.EnsureNotNull();
            var castTargetType = Context.GetTypeInfo(node.Type).Type.EnsureNotNull();

            if (castSourceType.SpecialType == castTargetType.SpecialType && castSourceType.SpecialType == SpecialType.System_Double)
            {
                /*
                 * Even though a cast from double => double can be view as an identity conversion (from the pov of the developer who wrote it)
                 * we still need to emit a *conv.r8* opcode. * (For more details see https://github.com/dotnet/roslyn/discussions/56198)
                 */
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Conv_R8);
                return;
            }

            if (castSourceType.SpecialType == castTargetType.SpecialType && castSourceType.SpecialType != SpecialType.None ||
                castSourceType.SpecialType == SpecialType.System_Byte && castTargetType.SpecialType == SpecialType.System_Char ||
                castSourceType.SpecialType == SpecialType.System_Byte && castTargetType.SpecialType == SpecialType.System_Int16 ||
                castSourceType.SpecialType == SpecialType.System_Byte && castTargetType.SpecialType == SpecialType.System_Int32 ||
                castSourceType.SpecialType == SpecialType.System_Char && castTargetType.SpecialType == SpecialType.System_Int32 ||
                castSourceType.SpecialType == SpecialType.System_Int16 && castTargetType.SpecialType == SpecialType.System_Int32)
                return;

            var conversion = Context.SemanticModel.ClassifyConversion(node.Expression, castTargetType, true);
            if (castTargetType.SpecialType != SpecialType.None && conversion.IsNumeric)
            {
                var opcode = castTargetType.SpecialType switch
                {
                    SpecialType.System_Int16 => OpCodes.Conv_I2,
                    SpecialType.System_Int32 => OpCodes.Conv_I4,
                    SpecialType.System_Int64 => castSourceType.SpecialType == SpecialType.System_Byte || castSourceType.SpecialType == SpecialType.System_Char ? OpCodes.Conv_U8 : OpCodes.Conv_I8,

                    SpecialType.System_Single => OpCodes.Conv_R4,
                    SpecialType.System_Double => OpCodes.Conv_R8,
                    SpecialType.System_Char => OpCodes.Conv_U2,
                    SpecialType.System_Byte => OpCodes.Conv_U1,

                    _ => throw new Exception($"Cast from {node.Expression} ({castSourceType}) to {castTargetType} is not supported.")
                };

                Context.ApiDriver.WriteCilInstruction(Context, ilVar, opcode);
            }
            else if (conversion.IsExplicit && conversion.IsReference)
            {
                var opcode = castTargetType.TypeKind == TypeKind.TypeParameter ? OpCodes.Unbox_Any : OpCodes.Castclass;
                AddCilInstruction(ilVar, opcode, castTargetType);
            }
            else if (conversion.IsBoxing || conversion.IsImplicit && conversion.IsReference && castSourceType.TypeKind == TypeKind.TypeParameter)
            {
                AddCilInstruction(ilVar, OpCodes.Box, castSourceType);
            }
            else if (conversion.IsUnboxing)
            {
                AddCilInstruction(ilVar, OpCodes.Unbox_Any, castTargetType);
            }
            else if (conversion.IsExplicit)
            {
                Context.AddCallToMethod(conversion.MethodSymbol, ilVar, MethodDispatchInformation.MostLikelyVirtual);
            }
        }

        public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var visitor = InterpolatedStringVisitor.For(node, Context, ilVar, this);
            node.Accept(visitor);
        }

        public override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var getTypeFromHandleSymbol = (IMethodSymbol) Context.RoslynTypeSystem.SystemType.GetMembers("GetTypeFromHandle").First();

            AddCilInstruction(ilVar, OpCodes.Ldtoken, Context.GetTypeInfo(node.Type).Type);
            string operand = getTypeFromHandleSymbol.MethodResolverExpression(Context);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Call, operand);
        }

        public override void VisitRangeExpression(RangeExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            VisitIndex(node.LeftOperand!);
            VisitIndex(node.RightOperand!);

            var indexType = Context.RoslynTypeSystem.SystemIndex;
            var rangeCtor = Context.RoslynTypeSystem.SystemRange
                ?.GetMembers(".ctor")
                .OfType<IMethodSymbol>()
                .Single(ctor => ctor.Parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, indexType) && SymbolEqualityComparer.Default.Equals(ctor.Parameters[1].Type, indexType));

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Newobj, rangeCtor.MethodResolverExpression(Context));

            void VisitIndex(ExpressionSyntax index)
            {
                using var _ = LineInformationTracker.Track(Context, index);
                index.Accept(this);
                switch (index.Kind())
                {
                    case SyntaxKind.NumericLiteralExpression:
                        index.InjectRequiredConversions(Context, ilVar);
                        break;

                    case SyntaxKind.IndexExpression:
                    case SyntaxKind.IdentifierName:
                        break; // A variable typed as System.Index, it has already been loaded when visiting the index...
                }
            }
        }

        public override void VisitThrowExpression(ThrowExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            CecilExpressionFactory.EmitThrow(Context, ilVar, node.Expression);
        }

        public override void VisitDefaultExpression(DefaultExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var type = Context.GetTypeInfo(node.Type).Type.EnsureNotNull();

            var defaultParent = node.Parent.EnsureNotNull<SyntaxNode, CSharpSyntaxNode>();
            var usageResult = defaultParent.Accept(UsageVisitor.GetInstance(Context));
            LoadLiteralValue(ilVar, type, type.ValueForDefaultLiteral(), usageResult, defaultParent);

            skipLeftSideVisitingInAssignment = (type.IsValueType || type.TypeKind == TypeKind.TypeParameter) && !type.IsPrimitiveType();
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => HandleLambdaExpression(node);
        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => HandleLambdaExpression(node);

        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            node.Expression.Accept(this);
            var t = Context.SemanticModel.GetTypeInfo(node.Expression).Type.EnsureNotNull();
            if (t.TypeKind == TypeKind.TypeParameter)
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Box, Context.TypeResolver.ResolveAny(t));
            }
            node.Pattern.Accept(this);
        }

        public override void VisitDeclarationPattern(DeclarationPatternSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            var varType = Context.TypeResolver.ResolveAny(Context.SemanticModel.GetTypeInfo(node.Type).Type);
            string localVarName = ((SingleVariableDesignationSyntax) node.Designation).Identifier.ValueText;
            var localVar = Context.AddLocalVariableToCurrentMethod(localVarName, varType).VariableName;

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Isinst, varType);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Stloc, localVar);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloc, localVar);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldnull);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Cgt);
        }

        public override void VisitRecursivePattern(RecursivePatternSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            var varType = Context.TypeResolver.ResolveAny(Context.SemanticModel.GetTypeInfo(node.Type!).Type);
            string localVarName = LocalVariableNameOrDefault(node, "tmp");
            var localVar = Context.AddLocalVariableToCurrentMethod(localVarName, varType).VariableName;

            var typeDoesNotMatchVar = CreateCilInstruction(ilVar, OpCodes.Ldc_I4_0);
            var typeMatchesVar = CreateCilInstruction(ilVar, OpCodes.Nop);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Isinst, varType);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Stloc, localVar);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloc, localVar);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Brfalse_S, typeDoesNotMatchVar);

            // Compares each sub-pattern
            foreach (var pattern in node.PropertyPatternClause!.Subpatterns)
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloc, localVar);
                pattern.Accept(this);

                var comparisonType = Context.SemanticModel.GetSymbolInfo(pattern.NameColon?.Name ?? pattern.ExpressionColon?.Expression!).Symbol!.GetMemberType();
                var opEquality = comparisonType.GetMembers().FirstOrDefault(m => m.Kind == SymbolKind.Method && m.Name == "op_Equality");
                if (opEquality != null)
                {
                    Context.AddCallToMethod(opEquality.EnsureNotNull<ISymbol, IMethodSymbol>(), ilVar, MethodDispatchInformation.MostLikelyVirtual);
                    Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Brfalse_S, typeDoesNotMatchVar);
                }
                else
                {
                    Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Bne_Un, typeDoesNotMatchVar);
                }
            }

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4_1); // if the execution (at runtime) reaches this point it means the 
                                                                 // pattern is a match
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Br_S, typeMatchesVar);
            AddCecilExpression($"{ilVar}.Append({typeDoesNotMatchVar});");
            AddCecilExpression($"{ilVar}.Append({typeMatchesVar});");

            string LocalVariableNameOrDefault(RecursivePatternSyntax toCheck, string defaultValue)
            {
                if (toCheck.Designation == null)
                    return defaultValue;

                return ((SingleVariableDesignationSyntax) toCheck.Designation).Identifier.ValueText;
            }
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            node.Expression.Accept(this);
            node.Expression.InjectRequiredConversions(Context, ilVar);
            if (node.Parent.IsKind(SyntaxKind.InvocationExpression))
                OnLastInstructionLoadingTargetOfInvocation();
            
            node.Name.Accept(this);
        }

        public override void VisitBaseExpression(BaseExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
        }

        public override void VisitCollectionExpression(CollectionExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            CollectionExpressionProcessor.Process(this, node);
        }
        
        public override void VisitAwaitExpression(AwaitExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitTupleExpression(TupleExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitSwitchExpression(SwitchExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitMakeRefExpression(MakeRefExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitRefTypeExpression(RefTypeExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitRefValueExpression(RefValueExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitCheckedExpression(CheckedExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitSizeOfExpression(SizeOfExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitWithExpression(WithExpressionSyntax node) => LogUnsupportedSyntax(node);

        private bool TryProcessMethodReferenceInObjectCreationExpression(BaseObjectCreationExpressionSyntax node)
        {
            var arg = node.ArgumentList?.Arguments.SingleOrDefault();
            if (arg != null && Context.SemanticModel.GetSymbolInfo(arg.Expression).Symbol is { Kind: SymbolKind.Method })
            {
                VisitIdentifierName((IdentifierNameSyntax) arg.Expression);
                return true;
            }

            return false;
        }

        private bool TryProcessTypeParameterInstantiation(BaseObjectCreationExpressionSyntax node)
        {
            var instantiatedType = Context.GetTypeInfo(node).Type;
            if (instantiatedType?.TypeKind != TypeKind.TypeParameter)
                return false;

            //call !!0 [System.Runtime]System.Activator::CreateInstance<!!T>()
            var openCreateInstanceMethod = Context.RoslynTypeSystem.SystemActivator.GetMembers("CreateInstance").OfType<IMethodSymbol>().Single(m => m.IsGenericMethod);
            var exps = openCreateInstanceMethod.MethodResolverExpression(Context)
                                                        .MakeGenericInstanceMethod(
                                                            Context, 
                                                            "CreateInstance", 
                                                            [Context.TypeResolver.ResolveAny(instantiatedType)], 
                                                            out var closedCreateInstanceMethod);
            Context.Generate(exps);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar,  OpCodes.Call, closedCreateInstanceMethod);
            return true;
        }

        private void ProcessPrefixPostfixOperators(ExpressionSyntax operand, OpCode opCode, bool isPrefix)
        {
            using var _ = LineInformationTracker.Track(Context, operand);
            Visit(operand);
            operand.InjectRequiredConversions(Context, ilVar);

            var assignmentVisitor = new AssignmentVisitor(Context, ilVar);

            var operandInfo = Context.SemanticModel.GetSymbolInfo(operand);
            if (operandInfo.Symbol != null && operandInfo.Symbol.Kind != SymbolKind.Field && operandInfo.Symbol.Kind != SymbolKind.Property) // Fields / Properties requires more complex handling to load the owning reference.
            {
                if (!isPrefix) // For *postfix* operators we duplicate the value *before* applying the operator...
                {
                    Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Dup);
                }

                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4_1);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, opCode);

                if (isPrefix) // For prefix operators we duplicate the value *after* applying the operator...
                {
                    Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Dup);
                }

                //assign (top of stack to the operand)
                assignmentVisitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
                operand.Accept(assignmentVisitor);

                return;
            }

            if (isPrefix)
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4_1);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, opCode);
            }

            var type = Context.SemanticModel.GetTypeInfo(operand).Type;
            var tempLocalName = StoreTopOfStackInLocalVariable(Context, ilVar, "tmp", type).VariableName;
            
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloc, tempLocalName);
            assignmentVisitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloc, tempLocalName);

            if (!isPrefix)
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4_1);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, opCode);
            }

            // assign (top of stack to the operand)
            operand.Accept(assignmentVisitor);
        }

        /*
         * Normal assignments usually requires visiting the *right* node first (i.e, the value to be assigned) and
         * then the *left* one (i.e, the target of the assignment)
         *
         * Some syntaxes (mostly involving value types) requires the order of visiting to be swapped, i.e, target of assignment
         * first and then the value. One example of such cases is when assigning `default` to a value type (DateTime d = default;)
         */
        private bool HandlePseudoAssignment(AssignmentExpressionSyntax node)
        {
            var lhsType = Context.SemanticModel.GetTypeInfo(node.Left).Type.EnsureNotNull();
            var isSimpleIndexAccess = SymbolEqualityComparer.Default.Equals(lhsType.OriginalDefinition, Context.RoslynTypeSystem.SystemIndex)
                                && node.Right.IsKind(SyntaxKind.IndexExpression)
                                && !node.Left.IsKind(SyntaxKind.SimpleMemberAccessExpression);

            var isNullAssignmentToNullableValueType = node.Right is LiteralExpressionSyntax { RawKind: (int) SyntaxKind.NullLiteralExpression }
                                && SymbolEqualityComparer.Default.Equals(lhsType.OriginalDefinition, Context.RoslynTypeSystem.SystemNullableOfT);

            var isRhsStructDefaultLiteralExpression = node.Right.IsKind(SyntaxKind.DefaultLiteralExpression) && lhsType.IsValueType;

            if (!isSimpleIndexAccess && !isRhsStructDefaultLiteralExpression && !isNullAssignmentToNullableValueType)
                return false;

            if (Context.SemanticModel.GetSymbolInfo(node.Left).Symbol is IParameterSymbol { RefKind: RefKind.Out or RefKind.Ref or RefKind.RefReadOnly })
                return false;

            using (Context.WithFlag<ContextFlagReseter>(Constants.ContextFlags.PseudoAssignmentToIndex))
                node.Left.Accept(this);

            node.Right.Accept(this);
            return true;

        }

        private void ProcessIndexerExpression(PrefixUnaryExpressionSyntax node)
        {
            var elementAccessExpression = node.Ancestors().OfType<ElementAccessExpressionSyntax>().FirstOrDefault();
            if (elementAccessExpression != null)
            {
                ProcessIndexerExpressionInElementAccessExpression(node, elementAccessExpression);
                return;
            }

            if (Context.HasFlag(Constants.ContextFlags.InRangeExpression))
            {
                node.Operand.Accept(this);
                return;
            }

            var ctor = Context.RoslynTypeSystem.SystemIndex
                .GetMembers(".ctor")
                .OfType<IMethodSymbol>()
                .Single(m => m.Parameters.Length == 2 && m.Parameters[0].Type.SpecialType == SpecialType.System_Int32 && m.Parameters[1].Type.SpecialType == SpecialType.System_Boolean);

            node.Operand.Accept(this);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4_1); // From end = true.

            /* 
             * node is something like '^3'
             * in this scenario we either need to 1) initialize some storage (that should be typed as System.Index) if such storage
             * already exists (for instance, we have an assignment to a parameter or a local variable) or 2)
             * instantiate a new System.Index (for example if we are returning this expression).
             * 
             * Note that when assigning to fields through member reference expression (for instance, a.b = ^2;), even though assignments
             * would normally be handled as *1* we need to handle it as *2* and instantiate a new System.Index.
             */
            var resolvedCtor = ctor.MethodResolverExpression(Context);
            var isAssignmentToMemberReference = node.Parent is AssignmentExpressionSyntax { Left.RawKind: (int) SyntaxKind.SimpleMemberAccessExpression };
            var isAutoPropertyInitialization = node.Parent is EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax };

            skipLeftSideVisitingInAssignment = !isAutoPropertyInitialization
                                               && !isAssignmentToMemberReference
                                               && !node.Parent.IsKind(SyntaxKind.RangeExpression)
                                               && (node.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression) || node.Parent.IsKind(SyntaxKind.EqualsValueClause));

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, skipLeftSideVisitingInAssignment ? OpCodes.Call : OpCodes.Newobj, resolvedCtor);

            var parentElementAccessExpression = node.Ancestors().OfType<ElementAccessExpressionSyntax>().FirstOrDefault(candidate => candidate.ArgumentList.Contains(node));
            if (parentElementAccessExpression != null)
            {
                var type = Context.SemanticModel.GetTypeInfo(node).Type;
                var tempLocal = StoreTopOfStackInLocalVariable(Context, ilVar, "tmpIndex", type).VariableName;
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloca, tempLocal);
            }
        }

        private void ProcessIndexerExpressionInElementAccessExpression(PrefixUnaryExpressionSyntax indexerExpression, ElementAccessExpressionSyntax elementAccessExpressionSyntax)
        {
            Utils.EnsureNotNull(elementAccessExpressionSyntax);

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Dup); // Duplicate the target of the element access expression

            var indexedType = Context.SemanticModel.GetTypeInfo(elementAccessExpressionSyntax.Expression).Type.EnsureNotNull();
            if (indexedType.Name == "Span")
            {
                var method = indexedType.GetMembers("Length").Single().EnsureNotNull<ISymbol, IPropertySymbol>().GetMethod;
                Context.AddCallToMethod(method, ilVar, MethodDispatchInformation.MostLikelyVirtual);
            }
            else
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldlen);
            }

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Conv_I4);
            indexerExpression.Operand.Accept(this);
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Sub);
        }

        private bool TryProcessInvocationOnParameterlessImplicitCtorOnValueType(BaseObjectCreationExpressionSyntax node, SymbolInfo ctorInfo)
        {
            if (ctorInfo.Symbol == null || ctorInfo.Symbol.IsImplicitlyDeclared == false || ctorInfo.Symbol.ContainingType.IsReferenceType)
                return false;

            var visitor = new ValueTypeNoArgCtorInvocationVisitor(Context, ilVar, node, ctorInfo);
            visitor.Visit(node.Parent);

            skipLeftSideVisitingInAssignment = visitor.TargetOfAssignmentIsValueType;
            return true;
        }

        private void ProcessOverloadedBinaryOperatorInvocation(BinaryExpressionSyntax node, IMethodSymbol method)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Visit(node.Left);
            Visit(node.Right);
            Context.AddCallToMethod(method, ilVar, MethodDispatchInformation.MostLikelyVirtual);
        }

        private void ProcessBinaryExpression(BinaryExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var handler = OperatorHandlerFor(node.OperatorToken);
            handler.Process(Context, ilVar, node, this);
            StoreTopOfStackInLocalVariableAndReloadItIfNeeded(node);
        }

        private void HandleLambdaExpression(LambdaExpressionSyntax node)
        {
            // We handle all lambdas as static, i.e, non-capturing.
            // use the lambda string representation to lookup the variable with the synthetic method definition
            var syntheticMethodVariable = Context.DefinitionVariables.GetVariable(node.GetSyntheticMethodName(), VariableMemberKind.Method, node.ToString());
            if (!syntheticMethodVariable.IsValid)
            {
                // if we fail to resolve the variable it means this is un unsupported scenario (like a lambda that captures context)
                LogUnsupportedSyntax(node);
                return;
            }

            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldnull);
            CecilDefinitionsFactory.InstantiateDelegate(Context, ilVar, Context.GetTypeInfo(node).ConvertedType!, syntheticMethodVariable.VariableName, new StaticDelegateCacheContext
            {
                IsStaticDelegate = false
            });
        }

        private void StoreTopOfStackInLocalVariableAndReloadItIfNeeded(ExpressionSyntax node)
        {
            var targetOfInvocationType = Context.SemanticModel.GetTypeInfo(node);
            if (targetOfInvocationType.Type?.IsValueType == false) 
                return; // it is not a value type...
            
            if( IsLeftHandSideOfMemberAccessExpression(node)
                || IsObjectCreationExpressionUsedAsSourceOfCast(node)
                || IsObjectCreationExpressionUsedAsInParameter(node)
                || IsSimpleMethodInvocation(node))
                StoreTopOfStackInLocalVariableAndLoad(node, targetOfInvocationType.Type!);
        }

        private static bool IsLeftHandSideOfMemberAccessExpression(ExpressionSyntax toBeChecked)
        {
            var parentMae = toBeChecked.FirstAncestorOrSelf<MemberAccessExpressionSyntax>(ancestor => ancestor.Kind() == SyntaxKind.SimpleMemberAccessExpression);
            if (parentMae == null)
                return false;
            
            var parentMaeExpressionIgnoringParenthesizedExpression = parentMae.Expression.DescendantNodesAndSelf().First(candidate => !candidate.IsKind(SyntaxKind.ParenthesizedExpression));
            return (parentMaeExpressionIgnoringParenthesizedExpression == toBeChecked) 
                   || (parentMae.Name == toBeChecked && parentMae.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression));
        }

        private static bool IsSimpleMethodInvocation(ExpressionSyntax toBeChecked) => toBeChecked.Parent.IsKind(SyntaxKind.InvocationExpression) && toBeChecked.IsKind(SyntaxKind.IdentifierName);

        private static bool IsObjectCreationExpressionUsedAsSourceOfCast(ExpressionSyntax node) => node.Parent.IsKind(SyntaxKind.CastExpression) && node.IsKind(SyntaxKind.ObjectCreationExpression);

        bool IsObjectCreationExpressionUsedAsInParameter(ExpressionSyntax toBeChecked)
        {
            if (!toBeChecked.Parent.IsKind(SyntaxKind.Argument) || !toBeChecked.IsKind(SyntaxKind.ObjectCreationExpression))
                return false;

            return ((ArgumentSyntax) toBeChecked.Parent).IsPassedAsInParameter(Context);
        }

        private void StoreTopOfStackInLocalVariableAndLoad(ExpressionSyntax expressionSyntax, ITypeSymbol type)
        {
            var tempLocalName = StoreTopOfStackInLocalVariable(Context, ilVar, "tmp", type).VariableName;
            if (!HandleLoadAddress(ilVar, type, expressionSyntax, OpCodes.Ldloca_S, tempLocalName))
            {
                // HandleLoadAddress() does not handle scenarios in which a value type instantiation is passed as an 
                // 'in parameter' to a method (that method is already complex, so I don't want to make it even more
                // complex)
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, RequiresAddressOfValue() ? OpCodes.Ldloca_S : OpCodes.Ldloc, tempLocalName);
            }

            var parentMae = expressionSyntax.FirstAncestorOrSelf<MemberAccessExpressionSyntax>(ancestor => ancestor.Kind() == SyntaxKind.SimpleMemberAccessExpression);
            if (parentMae != null && SymbolEqualityComparer.Default.Equals(Context.SemanticModel.GetSymbolInfo(parentMae).Symbol!.ContainingType, Context.RoslynTypeSystem.SystemValueType))
            {
                // it is a reference on a value type to a method defined in System.ValueType (GetHashCode(), ToString(), etc)
                Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, Context.TypeResolver.ResolveAny(type));
            }

            bool RequiresAddressOfValue() => IsObjectCreationExpressionUsedAsInParameter(expressionSyntax) || expressionSyntax.Parent!.FirstAncestorOrSelf<SyntaxNode>(c => !c.IsKind(SyntaxKind.ParenthesizedExpression)).IsKind(SyntaxKind.SimpleMemberAccessExpression);
        }
        
        private void FixCallSite()
        {
            Context.MoveLineAfter(callFixList.Pop(), Context.CurrentLine);
        }

        private void PushCall()
        {
            callFixList.Push(Context.CurrentLine);
        }

        private void ProcessProperty(SimpleNameSyntax node, IPropertySymbol propertySymbol)
        {
            propertySymbol.EnsurePropertyExists(Context, node);
            
            if (!propertySymbol.IsStatic && node.IsMemberAccessThroughImplicitThis())
            {
                // if this is an *unqualified* access we need to load *this*
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
            }

            var callOptions = node.Parent.MethodDispatchInformation();
            var parentMae = node.Parent as MemberAccessExpressionSyntax;
            if (node.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression) || parentMae != null && parentMae.Name.Identifier == node.Identifier && parentMae.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                Context.AddCallToMethod(propertySymbol.SetMethod, ilVar, callOptions);
            }
            else
            {
                HandlePropertyGetAccess(node, propertySymbol, callOptions);
            }
        }

        private void HandlePropertyGetAccess(SimpleNameSyntax node, IPropertySymbol propertySymbol, MethodDispatchInformation dispatchInformation)
        {
            if (propertySymbol.ContainingType.SpecialType == SpecialType.System_Array && propertySymbol.Name == "Length")
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldlen);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Conv_I4);
            }
            else if (propertySymbol.ContainingType.SpecialType == SpecialType.System_Array && propertySymbol.Name == "LongLength")
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldlen);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Conv_I8);
            }
            else
            {
                Context.AddCallToMethod(propertySymbol.GetMethod, ilVar, dispatchInformation);
                StoreTopOfStackInLocalVariableAndReloadItIfNeeded(node);
                HandlePotentialRefLoad(ilVar, node, propertySymbol.Type);
            }
        }

        private void ProcessMethodReference(SimpleNameSyntax node, IMethodSymbol method)
        {
            var invocationParent = node.Ancestors().OfType<InvocationExpressionSyntax>()
                .SingleOrDefault(i => i.Expression == node || i.Expression.ChildNodes().Contains(node));

            if (invocationParent != null)
            {
                ProcessMethodCall(node, method);
                return;
            }

            // this is not an invocation. We need to figure out whether this is an assignment, return, etc
            var firstParentNotPartOfName = node.Ancestors().First(a => !a.IsKind(SyntaxKind.QualifiedName)
                                                                       && !a.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                                                                       && !a.IsKind(SyntaxKind.EqualsValueClause)
                                                                       && !a.IsKind(SyntaxKind.VariableDeclarator));

            if (firstParentNotPartOfName is PrefixUnaryExpressionSyntax unaryPrefix && unaryPrefix.IsKind(SyntaxKind.AddressOfExpression))
            {
                string operand = method.MethodResolverExpression(Context);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldftn, operand);
                return;
            }

            var delegateType = firstParentNotPartOfName switch
            {
                // assumes that a method reference in an object creation expression can only be a delegate instantiation.
                ArgumentSyntax { Parent.Parent.RawKind: (int) SyntaxKind.ObjectCreationExpression } arg => Context.SemanticModel.GetTypeInfo(arg.Parent!.Parent!).Type,

                // assumes that a method reference in an invocation expression is method group -> delegate conversion. 
                ArgumentSyntax { Parent.Parent.RawKind: (int) SyntaxKind.InvocationExpression } arg => ((IMethodSymbol) Context.SemanticModel.GetSymbolInfo(arg.Parent!.Parent!).Symbol!).Parameters[arg.FirstAncestorOrSelf<ArgumentListSyntax>()!.Arguments.IndexOf(arg)].Type,

                AssignmentExpressionSyntax assignment => Context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol!.Accept(ElementTypeSymbolResolver.Instance),

                VariableDeclarationSyntax variableDeclaration => Context.SemanticModel.GetTypeInfo(variableDeclaration.Type).Type,

                ReturnStatementSyntax returnStatement => returnStatement.FirstAncestorOrSelf<MemberDeclarationSyntax>() switch
                {
                    MethodDeclarationSyntax md => Context.SemanticModel.GetTypeInfo(md.ReturnType).Type,
                    _ => throw new NotSupportedException($"Return is not supported.")
                },

                _ => throw new NotSupportedException($"Referencing method {method} in expression {firstParentNotPartOfName} ({firstParentNotPartOfName.GetType().FullName}) is not supported.")
            };

            // If we have a non-static method being referenced though an implicit "this" reference, load "this" onto stack 
            if (!method.IsStatic && !node.Parent.IsKind(SyntaxKind.ThisExpression) && node.Parent == firstParentNotPartOfName)
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
            }

            Debug.Assert(delegateType != null);
            // we have a reference to a method used to initialize a delegate
            // and need to load the referenced method token and instantiate the delegate. For instance:
            //IL_0002: ldarg.0
            //IL_0002: ldftn string Test::M(int32)
            //IL_0008: newobj instance void class [System.Private.CoreLib]System.Func`2<int32, string>::.ctor(object, native int)
            Context.EnsureForwardedMethod(method.OverriddenMethod ?? method.OriginalDefinition);
            CecilDefinitionsFactory.InstantiateDelegate(Context, ilVar, delegateType, method.MethodResolverExpression(Context), new StaticDelegateCacheContext()
            {
                IsStaticDelegate = method.IsStatic,
                Method = method,
                CacheBackingField = null,
                context = Context
            });
        }

        private void ProcessMethodCall(SimpleNameSyntax node, IMethodSymbol method)
        {
            // Local methods are always static.
            if (method.MethodKind != MethodKind.LocalFunction && !method.IsStatic && node.Parent.IsKind(SyntaxKind.InvocationExpression))
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
            }
            Context.EnsureForwardedMethod(method.OverriddenMethod ?? method.OriginalDefinition);

            var callOptions = node.Parent.MethodDispatchInformation();
            OnLastInstructionLoadingTargetOfInvocation();
            Context.AddCallToMethod(method, ilVar, callOptions);
        }

        private BinaryOperatorHandler OperatorHandlerFor(SyntaxToken operatorToken)
        {
            if (operatorHandlers.ContainsKey(operatorToken.Kind()))
            {
                return operatorHandlers[operatorToken.Kind()];
            }

            throw new Exception($"Operator '{operatorToken.ValueText}' not supported yet (expression: {operatorToken.Parent})");
        }

        /*
	     *            +--> ArgumentList
		 *            |
		 *       /---------\
		 * n.DoIt(10 + x, y);
		 * \----/
		 *	 |
		 *	 +---> Expression: MemberAccessExpression
		 * 
		 * We do not have a natural order to visit the expression and the argument list:
		 * 
		 * - If we visit in the order: arguments, expression (which would be the natural order)
		 *			push 10
		 *			push x
		 *			add
		 *			push y
		 *			push n <-- Should be the first
		 *			Call DoIt(Int32, Int32)
		 * 
		 * - If we visit in the order: expression, arguments
		 *			push n 
		 *			Call DoIt(Int32, Int32) <--+
		 *			push 10                    |
		 *			push x                     |  Should be here
		 *			add                        |
		 *			push y                     |
		 *			         <-----------------+
		 * 
		 * To fix this we visit in the order [exp, args] and move the call operation after visiting the arguments
		 */
        private void HandleMethodInvocation(SyntaxNode target, ArgumentListSyntax args, ISymbol? method = null)
        {
            var targetTypeInfo = Context.SemanticModel.GetTypeInfo(target).Type;
            if (targetTypeInfo?.TypeKind == TypeKind.FunctionPointer)
            {
                // if *target* is a function pointer, then we'll emmit a calli, which 
                // expects the stack as : <arg1, arg2, .. argn, function ptr>
                Visit(args);
                Visit(target);
            }
            else
            {
                Visit(target);
                var callContext = (FirstInstruction: _lastInstructionLoadingTargetOfInvocation, LastInstruction: Context.CurrentLine);

                StackallocAsArgumentFixer.Current?.MarkEndOfComputedCallTargetBlock(callContext.FirstInstruction);
                Debug.Assert(method != null);
                ProcessArgumentsTakingDefaultParametersIntoAccount(method, args);
                
                if (callContext.FirstInstruction != null)
                {
                    Context.MoveLinesToEnd(callContext.FirstInstruction, callContext.LastInstruction);
                    _lastInstructionLoadingTargetOfInvocation = null;
                }
            }
        }

        protected override void OnLastInstructionLoadingTargetOfInvocation()
        {
            _lastInstructionLoadingTargetOfInvocation = Context.CurrentLine;
        }
        
        private void ProcessArgumentsTakingDefaultParametersIntoAccount(ISymbol method, ArgumentListSyntax args)
        {
            Visit(args);
            if (method is not IMethodSymbol methodSymbol || methodSymbol.Parameters.Length <= args.Arguments.Count)
                return;

            foreach (var arg in methodSymbol.Parameters.Skip(args.Arguments.Count))
            {
                LoadLiteralValue(ilVar, arg.Type, ArgumentValueToUseForDefaultParameter(arg, methodSymbol.Parameters, args.Arguments), UsageResult.None, args);
            }
        }

        private string ArgumentValueToUseForDefaultParameter(IParameterSymbol arg, ImmutableArray<IParameterSymbol> parameters, SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            if (arg.TryGetAttribute<CallerArgumentExpressionAttribute>(out var callerArgumentExpressionAttribute))
            {
                var expressionParameter = parameters.SingleOrDefault(p => p.Name == (string) callerArgumentExpressionAttribute.ConstructorArguments[0].Value!);
                if (expressionParameter != null)
                    return arguments[expressionParameter.Ordinal].Expression.ToFullString();
            }

            return arg.ExplicitDefaultValue(rawString: true).Value;
        }

        private void HandleIdentifier(SimpleNameSyntax node)
        {
            using var trackIfNotPartOfTypeName = LineInformationTracker.Track(Context, node);
            var member = Context.SemanticModel.GetSymbolInfo(node);
            Debug.Assert(member.Symbol != null);
            switch (member.Symbol.Kind)
            {
                case SymbolKind.Method:
                    ProcessMethodReference(node, (IMethodSymbol) member.Symbol);
                    trackIfNotPartOfTypeName.Discard(); // for methods it is better to use the whole invocation expression.
                    break;

                case SymbolKind.Parameter:
                    ProcessParameter(ilVar, node, (IParameterSymbol) member.Symbol);
                    break;

                case SymbolKind.Local:
                    ProcessLocalVariable(ilVar, node, member.Symbol.EnsureNotNull<ISymbol, ILocalSymbol>());
                    break;

                case SymbolKind.Field:
                    ProcessField(ilVar, node, member.Symbol as IFieldSymbol);
                    break;

                case SymbolKind.Property:
                    ProcessProperty(node, (IPropertySymbol) member.Symbol);
                    break;

                case SymbolKind.TypeParameter:
                    Context.SetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, Context.TypeResolver.ResolveAny((ITypeSymbol) member.Symbol));
                    break;

                default:
                    trackIfNotPartOfTypeName.Discard();
                    break;
            }
        }

        private void ProcessImplicitArrayCreationExpression(InitializerExpressionSyntax initializer, ITypeSymbol typeSymbol)
        {
            var arrayType = typeSymbol.EnsureNotNull<ITypeSymbol, IArrayTypeSymbol>();
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4, initializer.Expressions.Count);
            ProcessArrayCreation(arrayType.ElementType, initializer);
        }

        private void ProcessArrayCreation(ITypeSymbol elementType, InitializerExpressionSyntax? initializer)
        {
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Newarr, Context.TypeResolver.ResolveAny(elementType).AsToken());
            if (PrivateImplementationDetailsGenerator.IsApplicableTo(initializer, Context))
                ArrayInitializationProcessor.InitializeOptimized(this, elementType, initializer.Expressions);
            else
                ArrayInitializationProcessor.InitializeUnoptimized(this, elementType, initializer?.Expressions, initializer != null ? Context.SemanticModel.GetOperation(initializer) : null);
        }
        string EmitTargetLabel(string relatedToName)
        {
            var instVarName = Context.Naming.Label(relatedToName);
            AddCecilExpression($"var {instVarName} = {ilVar}.Create({OpCodes.Nop.ConstantName()});");

            return instVarName;
        }

        private static void PopIfNotConsumed(IVisitorContext ctx, string ilVar, ExpressionSyntax node)
        {
            var nodeType = ctx.GetTypeInfo(node).Type.EnsureNotNull();
            if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && node.Kind().MapCompoundAssignment() == SyntaxKind.None
                && nodeType.SpecialType != SpecialType.System_Void)
            {
                ctx.ApiDriver.WriteCilInstruction(ctx, ilVar, OpCodes.Pop);
            }
        }

        private static void HandleModulusExpression(IVisitorContext context, string ilVar, ITypeSymbol lhs, ITypeSymbol rhs)
        {
            var l = lhs.GetMembers("op_Modulus").OfType<IMethodSymbol>().SingleOrDefault();
            var r = rhs.GetMembers("op_Modulus").OfType<IMethodSymbol>().SingleOrDefault();

            var operatorMethod = r ?? l;
            if (operatorMethod != null)
            {
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Call, operatorMethod.MethodResolverExpression(context));
            }
            else
            {
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Rem);
            }
        }
    }

    internal class ElementTypeSymbolResolver : SymbolVisitor<ITypeSymbol>
    {
        public static ElementTypeSymbolResolver Instance = new ElementTypeSymbolResolver();

        public override ITypeSymbol? VisitEvent(IEventSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol? VisitField(IFieldSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol? VisitLocal(ILocalSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol? VisitMethod(IMethodSymbol symbol)
        {
            return symbol.ReturnType.Accept(this);
        }

        public override ITypeSymbol? VisitProperty(IPropertySymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol? VisitParameter(IParameterSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol? VisitArrayType(IArrayTypeSymbol symbol)
        {
            return symbol.ElementType;
        }

        public override ITypeSymbol VisitNamedType(INamedTypeSymbol symbol)
        {
            return symbol;
        }
    }
}
