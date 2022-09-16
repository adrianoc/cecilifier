using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal partial class ExpressionVisitor : SyntaxWalkerBase
    {
        private static readonly IDictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>> operatorHandlers =
            new Dictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>>();
        
        private static readonly IDictionary<string, uint> predefinedTypeSize = new Dictionary<string, uint>();
        private static readonly IDictionary<SpecialType, OpCode> _opCodesForLdElem = new Dictionary<SpecialType, OpCode>()
        {
            [SpecialType.System_Byte] = OpCodes.Ldelem_I1,
            [SpecialType.System_Char] = OpCodes.Ldelem_I2,
            [SpecialType.System_Boolean] = OpCodes.Ldelem_U1,
            [SpecialType.System_Int16] = OpCodes.Ldelem_I2,
            [SpecialType.System_Int32] = OpCodes.Ldelem_I4,
            [SpecialType.System_Int64] = OpCodes.Ldelem_I8,
            [SpecialType.System_Single] = OpCodes.Ldelem_R4,
            [SpecialType.System_Double] = OpCodes.Ldelem_R8,
            [SpecialType.System_Object] = OpCodes.Ldelem_Ref,
            [SpecialType.System_String] = OpCodes.Ldelem_Ref,
            [SpecialType.None] = OpCodes.Ldelem_Ref,
        };
        
        private readonly string ilVar;
        private readonly Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();

        // if true, while visiting an AssignmentExpression its left side must not be visited.
        // this is used, for example, in value type ctor invocation in which case there's
        // no value in the stack to be stored after the ctor is run
        private bool skipLeftSideVisitingInAssignment; 

        static ExpressionVisitor()
        {
            predefinedTypeSize["int"] = sizeof(int);
            predefinedTypeSize["byte"] = sizeof(byte);
            predefinedTypeSize["long"] = sizeof(long);
            
            // Arithmetic operators
            operatorHandlers[SyntaxKind.PlusToken] = (ctx, ilVar, left, right) =>
            {
                if (left.SpecialType == SpecialType.System_String)
                {
                    var concatArgType = right.SpecialType == SpecialType.System_String ? "string" : "object";
                    ctx.EmitCilInstruction(ilVar, OpCodes.Call, $"assembly.MainModule.Import(typeof(string).GetMethod(\"Concat\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] {{ typeof({concatArgType}), typeof({concatArgType}) }}, null))");
                }
                else
                {
                    ctx.EmitCilInstruction(ilVar, OpCodes.Add);
                }
            };
            operatorHandlers[SyntaxKind.MinusToken] = (ctx, ilVar, left, right) => ctx.EmitCilInstruction(ilVar, OpCodes.Sub);
            operatorHandlers[SyntaxKind.AsteriskToken] = (ctx, ilVar, left, right) => ctx.EmitCilInstruction(ilVar, OpCodes.Mul);
            operatorHandlers[SyntaxKind.SlashToken] = (ctx, ilVar, left, right) => ctx.EmitCilInstruction(ilVar, OpCodes.Div);
            operatorHandlers[SyntaxKind.PercentToken] = HandleModulusExpression;
            
            operatorHandlers[SyntaxKind.GreaterThanToken] = (ctx, ilVar, left, right) => ctx.EmitCilInstruction(ilVar, CompareOpCodeFor(left));
            operatorHandlers[SyntaxKind.GreaterThanEqualsToken] = (ctx, ilVar, left, right) =>
            {
                ctx.EmitCilInstruction(ilVar, OpCodes.Clt);
                ctx.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                ctx.EmitCilInstruction(ilVar, OpCodes.Ceq);
            };
            operatorHandlers[SyntaxKind.LessThanEqualsToken] = (ctx, ilVar, left, right) =>
            {
                ctx.EmitCilInstruction(ilVar, OpCodes.Cgt);
                ctx.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                ctx.EmitCilInstruction(ilVar, OpCodes.Ceq);
            };
            operatorHandlers[SyntaxKind.EqualsEqualsToken] = (ctx, ilVar, left, right) => ctx.EmitCilInstruction(ilVar, OpCodes.Ceq);
            operatorHandlers[SyntaxKind.LessThanToken] = (ctx, ilVar, left, right) => ctx.EmitCilInstruction(ilVar, OpCodes.Clt);
            operatorHandlers[SyntaxKind.ExclamationEqualsToken] = (ctx, ilVar, left, right) =>
            {
                // This is not the most optimized way to handle != operator but it is generic and correct.
                ctx.EmitCilInstruction(ilVar, OpCodes.Ceq);
                ctx.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                ctx.EmitCilInstruction(ilVar, OpCodes.Ceq);
            };

            // Bitwise Operators
            operatorHandlers[SyntaxKind.AmpersandToken] = (ctx, ilVar, _, _) => ctx.EmitCilInstruction(ilVar, OpCodes.And);
            operatorHandlers[SyntaxKind.BarToken] = (ctx, ilVar, _, _) => ctx.EmitCilInstruction(ilVar, OpCodes.Or);
            operatorHandlers[SyntaxKind.CaretToken] = (ctx, ilVar, _, _) => ctx.EmitCilInstruction(ilVar, OpCodes.Xor);
            operatorHandlers[SyntaxKind.LessThanLessThanToken] = (ctx, ilVar, _, _) => ctx.EmitCilInstruction(ilVar, OpCodes.Shl);
            operatorHandlers[SyntaxKind.GreaterThanGreaterThanToken] = (ctx, ilVar, _, _) => ctx.EmitCilInstruction(ilVar, OpCodes.Shr);

            // Logical Operators
            operatorHandlers[SyntaxKind.AmpersandAmpersandToken] = (ctx, ilVar, _, _) => ctx.EmitCilInstruction(ilVar, OpCodes.And);
            operatorHandlers[SyntaxKind.BarBarToken] = (ctx, ilVar, _, _) => ctx.EmitCilInstruction(ilVar, OpCodes.Or);
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
            Utils.EnsureNotNull(node.Expression);
            node.Expression.Accept(this);
            InjectRequiredConversions(node.Expression);
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            node.Expression.Accept(this);
            InjectRequiredConversions(node.Expression);
        }

        public override void VisitInitializerExpression(InitializerExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            var typeInfo = Context.SemanticModel.GetTypeInfo(node.Parent);
            uint elementTypeSize = typeInfo.Type.SizeofArrayLikeItemElement();
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
                OpCode opCode = typeInfo.Type.Stind();
                Context.EmitCilInstruction(ilVar, opCode);
                offset += elementTypeSize;
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
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);
            }

            var arrayTypeSymbol = (IArrayTypeSymbol) Context.GetTypeInfo(node.Type).Type;
            ProcessArrayCreation(arrayTypeSymbol.ElementType, node.Initializer);
        }

        public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var arrayType = Context.GetTypeInfo(node);
            Utils.EnsureNotNull(arrayType.Type, $"Unable to infer array type: {node}");
            
            Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);

            var arrayTypeSymbol = (IArrayTypeSymbol) arrayType.Type;
            ProcessArrayCreation(arrayTypeSymbol.ElementType, node.Initializer);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var expressionInfo = Context.SemanticModel.GetSymbolInfo(node.Expression);
            if (expressionInfo.Symbol == null)
                return;

            var targetType = expressionInfo.Symbol.Accept(ElementTypeSymbolResolver.Instance);
            if (targetType.AssemblyQualifiedName().Equals("System.Span") && node.ArgumentList.Arguments.Count == 1 &&
                Context.GetTypeInfo(node.ArgumentList.Arguments[0].Expression).Type.AssemblyQualifiedName().Equals("System.Range"))
            {
                node.Accept(new ElementAccessExpressionWithRangeArgumentVisitor(Context, ilVar, this));
                return;
            }

            node.Expression.Accept(this);
            node.ArgumentList.Accept(this);

            var indexer = targetType.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(p => p.IsIndexer && p.Parameters.Length == node.ArgumentList.Arguments.Count);
            if (expressionInfo.Symbol.GetMemberType().Kind != SymbolKind.ArrayType && indexer != null)
            {
                indexer.EnsurePropertyExists(Context, node);
                AddMethodCall(ilVar, indexer.GetMethod);
                HandlePotentialRefLoad(ilVar, node, indexer.Type);
            }
            else if (targetType.IsValueType && !targetType.IsPrimitiveType())
            {
                AddCilInstruction(ilVar, OpCodes.Ldelem_Any, targetType);
            }
            else if (node.Parent.IsKind(SyntaxKind.RefExpression))
            {
                AddCilInstruction(ilVar, OpCodes.Ldelema, targetType);
            }
            else if (_opCodesForLdElem.TryGetValue(targetType.SpecialType, out var opCode))
            {
                Context.EmitCilInstruction(ilVar, opCode);
            }
        }
        
        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            base.VisitEqualsValueClause(node);
            InjectRequiredConversions(node.Value);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            if (HandlePseudoAssignment(node))
                return;
            
            var leftNodeMae = node.Left as MemberAccessExpressionSyntax;
            CSharpSyntaxNode exp = leftNodeMae?.Name ?? node.Left;
            // check if the left hand side of the assignment is a property (but not indexers) and handle that as a method (set) call.
            var visitor = new AssignmentVisitor(Context, ilVar, node);
            
            visitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
            Visit(node.Right);
            if (!skipLeftSideVisitingInAssignment)
            {
                visitor.Visit(node.Left);
            }

            var expSymbol = Context.SemanticModel.GetSymbolInfo(exp).Symbol;
            if (expSymbol is IEventSymbol @event)
            {
                AddMethodCall(ilVar, node.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken) ? @event.AddMethod : @event.RemoveMethod, node.IsAccessOnThisOrObjectCreation());
            }
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
            
            var localVarParent = (CSharpSyntaxNode) node.Parent;
            Debug.Assert(localVarParent != null);
                
            var nodeType = Context.SemanticModel.GetTypeInfo(node);
            LoadLiteralValue(ilVar, nodeType.Type ?? nodeType.ConvertedType, node.Token.Text, localVarParent.Accept(UsageVisitor.GetInstance(Context)) == UsageKind.CallTarget);
        }

        public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.Parent is ArgumentSyntax argument && argument.RefKindKeyword.Kind() == SyntaxKind.OutKeyword)
            {
                var localSymbol = (ILocalSymbol) Context.SemanticModel.GetSymbolInfo(node).Symbol;
                var designation = ((SingleVariableDesignationSyntax) node.Designation);
                var resolvedOutArgType = Context.TypeResolver.Resolve(localSymbol.Type);
                
                var outLocalName = AddLocalVariableWithResolvedType(
                    designation.Identifier.Text,
                    Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method),
                    resolvedOutArgType
                );

                Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, outLocalName);
            }
            
            base.VisitDeclarationExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            using var __ = LineInformationTracker.Track(Context, node);
            using var _ = StackallocAsArgumentFixer.TrackPassingStackAllocToSpanArgument(Context, node, ilVar);
            var constantValue = Context.SemanticModel.GetConstantValue(node);
            if (constantValue.HasValue && node.Expression is IdentifierNameSyntax { Identifier: { Text: "nameof" }} nameofExpression)
            {
                string operand = $"\"{node.ArgumentList.Arguments[0].ToFullString()}\"";
                Context.EmitCilInstruction(ilVar, OpCodes.Ldstr, operand);
                return;
            }

            HandleMethodInvocation(node.Expression, node.ArgumentList, Context.SemanticModel.GetSymbolInfo(node.Expression).Symbol);
            StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(node);
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
                Context.EmitCilInstruction(ilVar, OpCodes.Dup);
            
            Context.EmitCilInstruction(ilVar, OpCodes.Brtrue_S, whenTrueLabel);
         
            if (!targetEvaluationDoNotHaveSideEffects)
                Context.EmitCilInstruction(ilVar, OpCodes.Pop);
            
            // code to handle null case 
            var currentMethodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            var expressionTypeInfo = Context.SemanticModel.GetTypeInfo(node);
            var resolvedConcreteNullableType = Context.TypeResolver.Resolve(expressionTypeInfo.Type);
            var tempNullableVar = AddLocalVariableWithResolvedType("nullable", currentMethodVar, resolvedConcreteNullableType);

            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, tempNullableVar);   
            Context.EmitCilInstruction(ilVar, OpCodes.Initobj, resolvedConcreteNullableType);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, tempNullableVar);
            Context.EmitCilInstruction(ilVar, OpCodes.Br, conditionalEnd);
            
            // handle not null case...
            AddCecilExpression("{0}.Append({1});", ilVar, whenTrueLabel);
            if (targetEvaluationDoNotHaveSideEffects)
                node.Expression.Accept(this);
            
            node.WhenNotNull.Accept(this);
            
            var nullableCtor = expressionTypeInfo.Type?.GetMembers(".ctor")
                .OfType<IMethodSymbol>()
                .Single(method => method.Parameters.Length == 1 && method.Parameters[0].Type.MetadataToken == ((INamedTypeSymbol) expressionTypeInfo.Type).TypeArguments[0].MetadataToken);
            
            Context.EmitCilInstruction(ilVar, 
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
            Context.EmitCilInstruction(ilVar, OpCodes.Brfalse_S, whenFalse);

            Visit(node.WhenTrue);
            Context.EmitCilInstruction(ilVar, OpCodes.Br_S, conditionEnd);

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
            
            base.VisitArgument(node);
            
            InjectRequiredConversions(node.Expression, () =>
            {
                AddCecilExpression(last.Value.Replace("\t", string.Empty).Replace("\n", String.Empty));
            });
            
            StackallocAsArgumentFixer.Current?.StoreTopOfStackToLocalVariable(Context.SemanticModel.GetTypeInfo(node.Expression).Type);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            if (node.IsOperatorOnCustomUserType(Context.SemanticModel, out var method))
            {
                Visit(node.Operand);
                AddMethodCall(ilVar, method);
                return;
            }
            
            if (node.OperatorToken.Kind() == SyntaxKind.AmpersandToken)
            {
                Visit(node.Operand);
            }
            else if (node.IsKind(SyntaxKind.UnaryMinusExpression))
            {
                Visit(node.Operand);
                InjectRequiredConversions(node.Operand);
                Context.EmitCilInstruction(ilVar, OpCodes.Neg);
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
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_0);
                Context.EmitCilInstruction(ilVar, OpCodes.Ceq);
            }
            else if (node.IsKind(SyntaxKind.BitwiseNotExpression))
            {
                node.Operand.Accept(this);
                Context.EmitCilInstruction(ilVar, OpCodes.Not);
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
                AddMethodCall(ilVar, method);
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

            var localVarParent = (CSharpSyntaxNode) node.Parent;
            if (localVarParent.Accept(UsageVisitor.GetInstance(Context)) != UsageKind.CallTarget)
                return;

            StoreTopOfStackInLocalVariableAndLoadItsAddress(Context.RoslynTypeSystem.SystemInt32);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            //TODO: Refactor to reuse code from VisitIdentifierName....
            var ctorInfo = Context.SemanticModel.GetSymbolInfo(node);

            var ctor = (IMethodSymbol) ctorInfo.Symbol;
            if (TryProcessInvocationOnParameterlessImplicitCtorOnValueType(node, ctorInfo))
            {
                return;
            }

            var varName = Context.Naming.SyntheticVariable(ctor.ContainingType.Name, ElementKind.LocalVariable);
            EnsureForwardedMethod(Context, varName, ctor, Array.Empty<TypeParameterSyntax>());

            string operand = ctor.MethodResolverExpression(Context);
            Context.EmitCilInstruction(ilVar, OpCodes.Newobj, operand);
            PushCall();

            Visit(node.ArgumentList);
            FixCallSite();
            
            StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(node);
        }
        
        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            base.Visit(node.Expression);
            PopIfNotConsumed(Context, ilVar, node.Expression);
        }

        public override void VisitThisExpression(ThisExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
            base.VisitThisExpression(node);
        }

        public override void VisitRefExpression(RefExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            using (Context.WithFlag(Constants.ContextFlags.RefReturn))
            {
                node.Expression.Accept(this);
            }
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            
            node.Expression.Accept(this);
            var castSource = Context.GetTypeInfo(node.Expression);
            var castTarget = Context.GetTypeInfo(node.Type);
            
            Utils.EnsureNotNull(castSource.Type,$"Failed to get type information for expression: {node.Expression}");
            Utils.EnsureNotNull(castTarget.Type, $"Failed to get type information for: {node.Type}");

            if (castSource.Type.SpecialType == castTarget.Type.SpecialType && castSource.Type.SpecialType == SpecialType.System_Double)
            {
                /*
                 * Even though a cast from double => double can be view as an identity conversion (from the pov of the developer who wrote it)
                 * we still need to emit a *conv.r8* opcode. * (For more details see https://github.com/dotnet/roslyn/discussions/56198)
                 */
                Context.EmitCilInstruction(ilVar, OpCodes.Conv_R8);
                return;
            }
            
            if (castSource.Type.SpecialType == castTarget.Type.SpecialType && castSource.Type.SpecialType != SpecialType.None ||
                castSource.Type.SpecialType == SpecialType.System_Byte && castTarget.Type.SpecialType == SpecialType.System_Char ||
                castSource.Type.SpecialType == SpecialType.System_Byte && castTarget.Type.SpecialType == SpecialType.System_Int16 ||
                castSource.Type.SpecialType == SpecialType.System_Byte && castTarget.Type.SpecialType == SpecialType.System_Int32 ||
                castSource.Type.SpecialType == SpecialType.System_Char && castTarget.Type.SpecialType == SpecialType.System_Int32 ||
                castSource.Type.SpecialType == SpecialType.System_Int16 && castTarget.Type.SpecialType == SpecialType.System_Int32)
                return;
            
            var conversion = Context.SemanticModel.ClassifyConversion(node.Expression, castTarget.Type, true);
            if (castTarget.Type.SpecialType != SpecialType.None && conversion.IsNumeric)
            {
                var opcode = castTarget.Type.SpecialType switch
                {
                    SpecialType.System_Int16 => OpCodes.Conv_I2,
                    SpecialType.System_Int32 => OpCodes.Conv_I4,
                    SpecialType.System_Int64 => castSource.Type.SpecialType == SpecialType.System_Byte || castSource.Type.SpecialType == SpecialType.System_Char ? OpCodes.Conv_U8 : OpCodes.Conv_I8,
            
                    SpecialType.System_Single => OpCodes.Conv_R4,
                    SpecialType.System_Double => OpCodes.Conv_R8,
                    SpecialType.System_Char => OpCodes.Conv_U2,
                    SpecialType.System_Byte => OpCodes.Conv_U1,
            
                    _ => throw new Exception($"Cast from {node.Expression} ({castSource.Type}) to {castTarget.Type} is not supported.")
                };
            
                Context.EmitCilInstruction(ilVar, opcode);
            }
            else if (conversion.IsExplicit && conversion.IsReference)
            {
                var opcode = castTarget.Type.TypeKind == TypeKind.TypeParameter ? OpCodes.Unbox_Any : OpCodes.Castclass;
                AddCilInstruction(ilVar, opcode, castTarget.Type);
            }
            else if (conversion.IsImplicit && conversion.IsReference && castSource.Type.TypeKind == TypeKind.TypeParameter)
            {
                AddCilInstruction(ilVar, OpCodes.Box, castSource.Type);
            }
            else if (conversion.IsExplicit)
            {
                AddMethodCall(ilVar, conversion.MethodSymbol);
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
            Context.EmitCilInstruction(ilVar, OpCodes.Call, operand);
        }
        
        public override void VisitRangeExpression(RangeExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);

            VisitIndex(node.LeftOperand);
            VisitIndex(node.RightOperand);

            var indexType = Context.RoslynTypeSystem.SystemIndex;
            var rangeCtor = Context.RoslynTypeSystem.SystemRange
                ?.GetMembers(".ctor")
                .OfType<IMethodSymbol>()
                .Single(ctor => ctor.Parameters.Length == 2 && ctor.Parameters[0].Type == indexType && ctor.Parameters[1].Type == indexType);
            
            Context.EmitCilInstruction(ilVar, OpCodes.Newobj, rangeCtor.MethodResolverExpression(Context));

            void VisitIndex(ExpressionSyntax index)
            {
                using var _ = LineInformationTracker.Track(Context, index);
                index.Accept(this);
                switch (index.Kind())
                {
                    case SyntaxKind.NumericLiteralExpression:
                        InjectRequiredConversions(index);
                        break;
                    
                    case SyntaxKind.IndexExpression:
                    case SyntaxKind.IdentifierName: break; // A variable typed as System.Index, it has already been loaded when visiting the index...
                }
            }
        }
        
        public override void VisitThrowExpression(ThrowExpressionSyntax node)
        {
            CecilExpressionFactory.EmitThrow(Context, ilVar, node.Expression);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => HandleLambdaExpression(node);
        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => HandleLambdaExpression(node);
        public override void VisitAwaitExpression(AwaitExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitTupleExpression(TupleExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitSwitchExpression(SwitchExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitMakeRefExpression(MakeRefExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitRefTypeExpression(RefTypeExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitRefValueExpression(RefValueExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitCheckedExpression(CheckedExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitDefaultExpression(DefaultExpressionSyntax node) => LogUnsupportedSyntax(node);
        public override void VisitSizeOfExpression(SizeOfExpressionSyntax node) => LogUnsupportedSyntax(node);

        private void ProcessPrefixPostfixOperators(ExpressionSyntax operand, OpCode opCode, bool isPrefix)
        {
            using var _ = LineInformationTracker.Track(Context, operand);
            Visit(operand);
            InjectRequiredConversions(operand);

            var assignmentVisitor = new AssignmentVisitor(Context, ilVar);

            var operandInfo = Context.SemanticModel.GetSymbolInfo(operand);
            if (operandInfo.Symbol != null && operandInfo.Symbol.Kind != SymbolKind.Field && operandInfo.Symbol.Kind != SymbolKind.Property) // Fields / Properties requires more complex handling to load the owning reference.
            {
                if (!isPrefix) // For *postfix* operators we duplicate the value *before* applying the operator...
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Dup);
                }

                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_1);
                Context.EmitCilInstruction(ilVar, opCode);

                if (isPrefix) // For prefix operators we duplicate the value *after* applying the operator...
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Dup);
                }
                
                //assign (top of stack to the operand)
                assignmentVisitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
                operand.Accept(assignmentVisitor);

                return;
            }

            if (isPrefix)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_1);
                Context.EmitCilInstruction(ilVar, opCode);
            }

            var tempLocalName = AddLocalVariableWithResolvedType("tmp", Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method), Context.TypeResolver.Resolve(Context.SemanticModel.GetTypeInfo(operand).Type));
            Context.EmitCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
            assignmentVisitor.InstructionPrecedingValueToLoad = Context.CurrentLine;
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, tempLocalName);
            
            if (!isPrefix)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_1);
                Context.EmitCilInstruction(ilVar, opCode);
            }
            
            // assign (top of stack to the operand)
            operand.Accept(assignmentVisitor);
        }

        private bool HandlePseudoAssignment(AssignmentExpressionSyntax node)
        {
            var lhsType = Context.SemanticModel.GetTypeInfo(node.Left);
            if (lhsType.Type.AssemblyQualifiedName() != "System.Index" || !node.Right.IsKind(SyntaxKind.IndexExpression) || node.Left.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                return false;

            using (Context.WithFlag(Constants.ContextFlags.PseudoAssignmentToIndex))
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
            Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4_1); // From end = true.

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
            var isAssignmentToMemberReference = node.Parent is AssignmentExpressionSyntax { Left.RawKind: (int)SyntaxKind.SimpleMemberAccessExpression };
            var isAutoPropertyInitialization = node.Parent is EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax };
            
            skipLeftSideVisitingInAssignment = !isAutoPropertyInitialization 
                                               && !isAssignmentToMemberReference 
                                               && !node.Parent.IsKind(SyntaxKind.RangeExpression) 
                                               && (node.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression) || node.Parent.IsKind(SyntaxKind.EqualsValueClause));
            
            Context.EmitCilInstruction(ilVar, skipLeftSideVisitingInAssignment ? OpCodes.Call : OpCodes.Newobj, resolvedCtor);

            var parentElementAccessExpression = node.Ancestors().OfType<ElementAccessExpressionSyntax>().FirstOrDefault(candidate => candidate.ArgumentList.Contains(node));
            if (parentElementAccessExpression != null)
            {
                var tempLocal = AddLocalVariableToCurrentMethod("tmpIndex", Context.TypeResolver.Resolve(Context.SemanticModel.GetTypeInfo(node).Type));
                Context.EmitCilInstruction(ilVar, OpCodes.Stloc, tempLocal);
                Context.EmitCilInstruction(ilVar, OpCodes.Ldloca, tempLocal);
            }
        }

        private void ProcessIndexerExpressionInElementAccessExpression(PrefixUnaryExpressionSyntax indexerExpression, ElementAccessExpressionSyntax elementAccessExpressionSyntax)
        {
            Utils.EnsureNotNull(elementAccessExpressionSyntax);

            Context.EmitCilInstruction(ilVar, OpCodes.Dup); // Duplicate the target of the element access expression

            var indexed = Context.SemanticModel.GetTypeInfo(elementAccessExpressionSyntax.Expression);
            Utils.EnsureNotNull(indexed.Type, "Cannot be null.");
            if (indexed.Type.Name == "Span")
            {
                AddMethodCall(ilVar, ((IPropertySymbol)indexed.Type.GetMembers("Length").Single()).GetMethod);
            }
            else
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldlen);
            }

            Context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
            indexerExpression.Operand.Accept(this);
            Context.EmitCilInstruction(ilVar, OpCodes.Sub);
        }
        
        private bool TryProcessInvocationOnParameterlessImplicitCtorOnValueType(ObjectCreationExpressionSyntax node, SymbolInfo ctorInfo)
        {
            if (ctorInfo.Symbol?.IsImplicitlyDeclared == false || ctorInfo.Symbol.ContainingType.IsReferenceType)
                return false;

            new ValueTypeNoArgCtorInvocationVisitor(Context, ilVar, ctorInfo).Visit(node.Parent);
            return skipLeftSideVisitingInAssignment = true;
        }
        
        private void ProcessOverloadedBinaryOperatorInvocation(BinaryExpressionSyntax node, IMethodSymbol method)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Visit(node.Left);
            Visit(node.Right);
            AddMethodCall(ilVar, method, false);
        }

        private void ProcessBinaryExpression(BinaryExpressionSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            Visit(node.Left);
            InjectRequiredConversions(node.Left);

            Visit(node.Right);
            InjectRequiredConversions(node.Right);

            var handler = OperatorHandlerFor(node.OperatorToken);
            handler(
                Context,
                ilVar,
                Context.SemanticModel.GetTypeInfo(node.Left).Type,
                Context.SemanticModel.GetTypeInfo(node.Right).Type);
        }
        
        private void HandleLambdaExpression(LambdaExpressionSyntax node)
        {
            //TODO: Handle static lambdas.
            // use the lambda string representation to lookup the variable with the synthetic method definition 
            var syntheticMethodVariable = Context.DefinitionVariables.GetVariable(node.ToString(), VariableMemberKind.Method);
            if (!syntheticMethodVariable.IsValid)
            {
                // if we fail to resolve the variable it means this is un unsupported scenario (like a lambda that captures context)
                LogUnsupportedSyntax(node);
                return;
            }

            Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
            CecilDefinitionsFactory.InstantiateDelegate(Context, ilVar, Context.GetTypeInfo(node).ConvertedType, syntheticMethodVariable.VariableName);
        }
        
        private void StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(ExpressionSyntax node)
        {
            var invocation = (InvocationExpressionSyntax) node.Ancestors().FirstOrDefault(a => a.IsKind(SyntaxKind.InvocationExpression));
            if (invocation == null || invocation.ArgumentList.Arguments.Any(argumentExp => argumentExp.Expression.DescendantNodesAndSelf().Any( exp => exp == node)))
                return;

            var targetOfInvocationType = Context.SemanticModel.GetTypeInfo(node);
            if (targetOfInvocationType.Type?.IsValueType == false)
                return;

            StoreTopOfStackInLocalVariableAndLoadItsAddress(targetOfInvocationType.Type);
        }

        private void StoreTopOfStackInLocalVariableAndLoadItsAddress(ITypeSymbol type)
        {
            var tempLocalName = AddLocalVariableWithResolvedType("tmp", Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method), Context.TypeResolver.Resolve(type));
            Context.EmitCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
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
            
            var parentMae = node.Parent as MemberAccessExpressionSyntax;
            var isAccessOnThisOrObjectCreation = true;
            if (parentMae != null)
            {
                isAccessOnThisOrObjectCreation = parentMae.Expression.IsKind(SyntaxKind.ObjectCreationExpression);
            }

            // if this is an *unqualified* access we need to load *this*
            if ((parentMae == null || parentMae.Expression == node) && !node.Parent.IsKind(SyntaxKind.MemberBindingExpression))
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            var parentExp = node.Parent;
            if (parentExp.Kind() == SyntaxKind.SimpleAssignmentExpression || parentMae != null && parentMae.Name.Identifier == node.Identifier && parentMae.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                AddMethodCall(ilVar, propertySymbol.SetMethod, isAccessOnThisOrObjectCreation);
            }
            else
            {
                if (propertySymbol.ContainingType.SpecialType == SpecialType.System_Array && propertySymbol.Name == "Length")
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldlen);
                    Context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
                }
                else if (propertySymbol.ContainingType.SpecialType == SpecialType.System_Array && propertySymbol.Name == "LongLength")
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldlen);
                    Context.EmitCilInstruction(ilVar, OpCodes.Conv_I8);
                }
                else
                {
                    AddMethodCall(ilVar, propertySymbol.GetMethod, isAccessOnThisOrObjectCreation);
                    StoreTopOfStackInLocalVariableAndLoadItsAddressIfNeeded(node);
                    HandlePotentialRefLoad(ilVar, node, propertySymbol.Type);
                }
            }
        }

        private void ProcessField(SimpleNameSyntax node, IFieldSymbol fieldSymbol)
        {
            var nodeParent = (CSharpSyntaxNode) node.Parent;
            Debug.Assert(nodeParent != null);
                         
            if (fieldSymbol.HasConstantValue && fieldSymbol.IsConst)
            {
                LoadLiteralToStackHandlingCallOnValueTypeLiterals(
                    ilVar,
                    fieldSymbol.Type,
                    fieldSymbol.Type.SpecialType switch
                    {
                        SpecialType.System_String => $"\"{fieldSymbol.ConstantValue}\"",
                        SpecialType.System_Boolean => (bool) fieldSymbol.ConstantValue ? 1 : 0,
                        _ => fieldSymbol.ConstantValue 
                    },
                    nodeParent.Accept(UsageVisitor.GetInstance(Context)) == UsageKind.CallTarget);
                return;
            }
            
            var fieldDeclarationVariable = fieldSymbol.EnsureFieldExists(Context, node);

            var isTargetOfQualifiedAccess = (node.Parent is MemberAccessExpressionSyntax mae) && mae.Name == node;
            if (!fieldSymbol.IsStatic && !isTargetOfQualifiedAccess)
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
            
            if (HandleLoadAddress(ilVar, fieldSymbol.Type, (CSharpSyntaxNode) node.Parent, fieldSymbol.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, fieldSymbol.Name, VariableMemberKind.Field, fieldSymbol.ContainingType.ToDisplayString()))
            {
                return;
            }

            if (fieldSymbol.IsVolatile)
                Context.EmitCilInstruction(ilVar, OpCodes.Volatile);

            var resolvedField = fieldDeclarationVariable.IsValid
                ? fieldDeclarationVariable.VariableName
                : fieldSymbol.FieldResolverExpression(Context);

            var opCode = fieldSymbol.LoadOpCodeForFieldAccess();
            Context.EmitCilInstruction(ilVar, opCode, resolvedField);

            EmitBoxOpCodeIfCallOnTypeParameter(fieldSymbol.Type, nodeParent);
            HandlePotentialDelegateInvocationOn(node, fieldSymbol.Type, ilVar);
        }
        
        private void ProcessLocalVariable(SimpleNameSyntax localVarSyntax, SymbolInfo varInfo)
        {
            var symbol = varInfo.Symbol.EnsureNotNull<ISymbol, ILocalSymbol>();
            var localVar = (CSharpSyntaxNode) localVarSyntax.Parent;
            if (HandleLoadAddress(ilVar, symbol.Type, localVar, OpCodes.Ldloca, symbol.Name, VariableMemberKind.LocalVariable))
                return;

            var operand = Context.DefinitionVariables.GetVariable(symbol.Name, VariableMemberKind.LocalVariable).VariableName;
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, operand);
            
            EmitBoxOpCodeIfCallOnTypeParameter(symbol.Type, localVar);
            HandlePotentialDelegateInvocationOn(localVarSyntax, symbol.Type, ilVar);
            HandlePotentialFixedLoad(symbol);
            HandlePotentialRefLoad(ilVar, localVarSyntax, symbol.Type);
        }

        private void EmitBoxOpCodeIfCallOnTypeParameter(ITypeSymbol type, CSharpSyntaxNode localVar)
        {
            if (type.TypeKind == TypeKind.TypeParameter && localVar.Accept(UsageVisitor.GetInstance(Context)) == UsageKind.CallTarget)
                Context.EmitCilInstruction(ilVar, OpCodes.Box, Context.TypeResolver.Resolve(type));
        }

        private void HandlePotentialFixedLoad(ILocalSymbol symbol)
        {
            if (!symbol.IsFixed)
                return;

            Context.EmitCilInstruction(ilVar, OpCodes.Conv_U);
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
            var firstParentNotPartOfName = node.Ancestors().First(a => a.Kind() != SyntaxKind.QualifiedName 
                                                                       && a.Kind() != SyntaxKind.SimpleMemberAccessExpression
                                                                       && a.Kind() != SyntaxKind.EqualsValueClause
                                                                       && a.Kind() != SyntaxKind.VariableDeclarator);

            if (firstParentNotPartOfName is PrefixUnaryExpressionSyntax unaryPrefix && unaryPrefix.IsKind(SyntaxKind.AddressOfExpression))
            {
                string operand = method.MethodResolverExpression(Context);
                Context.EmitCilInstruction(ilVar, OpCodes.Ldftn, operand);
                return;
            }
                
            var delegateType = firstParentNotPartOfName switch
            {
                ArgumentSyntax arg => ((IMethodSymbol) Context.SemanticModel.GetSymbolInfo(arg.Parent.Parent).Symbol).Parameters[arg.FirstAncestorOrSelf<ArgumentListSyntax>().Arguments.IndexOf(arg)].Type,
                
                AssignmentExpressionSyntax assignment => Context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol.Accept(ElementTypeSymbolResolver.Instance),
                    
                VariableDeclarationSyntax variableDeclaration => Context.SemanticModel.GetTypeInfo(variableDeclaration.Type).Type,
                    
                ReturnStatementSyntax returnStatement => returnStatement.FirstAncestorOrSelf<MemberDeclarationSyntax>() switch
                {
                    MethodDeclarationSyntax md => Context.SemanticModel.GetTypeInfo(md.ReturnType).Type,
                    _ => throw new NotSupportedException($"Return is not supported.")
                },
                        
                _ => throw new NotSupportedException($"Referencing method {method} in expression {firstParentNotPartOfName} ({firstParentNotPartOfName.GetType().FullName}) is not supported.")
            };
                
            // we have a reference to a method used to initialize a delegate
            // and need to load the referenced method token and instantiate the delegate. For instance:
            //IL_0002: ldarg.0
            //IL_0002: ldftn string Test::M(int32)
            //IL_0008: newobj instance void class [System.Private.CoreLib]System.Func`2<int32, string>::.ctor(object, native int)

            if (method.IsStatic)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldnull);
            }
            else if (!node.Parent.IsKind(SyntaxKind.ThisExpression) && node.Parent == firstParentNotPartOfName)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            EnsureForwardedMethod(Context, Context.Naming.SyntheticVariable(node.Identifier.Text, ElementKind.Method), method.OverriddenMethod ?? method.OriginalDefinition, Array.Empty<TypeParameterSyntax>());
            CecilDefinitionsFactory.InstantiateDelegate(Context, ilVar, delegateType, method.MethodResolverExpression(Context));
        }
        
        private void ProcessMethodCall(SimpleNameSyntax node, IMethodSymbol method)
        {
            // Local methods are always static.
            if (method.MethodKind != MethodKind.LocalFunction && !method.IsStatic && method.IsDefinedInCurrentType(Context) && node.Parent.Kind() == SyntaxKind.InvocationExpression)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            //TODO: We need to find the InvocationSyntax that node represents...
            EnsureForwardedMethod(Context, Context.Naming.SyntheticVariable(node.Identifier.Text, ElementKind.Method), method.OverriddenMethod ?? method.OriginalDefinition, Array.Empty<TypeParameterSyntax>());
            var isAccessOnThis = !node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression);

            var mae = node.Parent as MemberAccessExpressionSyntax;
            if (mae?.Expression.IsKind(SyntaxKind.ObjectCreationExpression) == true)
            {
                isAccessOnThis = true;
            }

            AddMethodCall(ilVar, method, isAccessOnThis);
        }

        private void InjectRequiredConversions(ExpressionSyntax expression, Action loadArrayIntoStack = null)
        {
            var typeInfo = Context.SemanticModel.GetTypeInfo(expression);
            if (typeInfo.Type == null)
                return;
            
            var conversion = Context.SemanticModel.GetConversion(expression);
            if (conversion.IsImplicit)
            {
                if (conversion.IsNumeric)
                {
                    Debug.Assert(typeInfo.ConvertedType != null);
                    switch (typeInfo.ConvertedType.SpecialType)
                    {
                        case SpecialType.System_Single:
                            Context.EmitCilInstruction(ilVar, OpCodes.Conv_R4);
                            return;

                        case SpecialType.System_Double:
                            Context.EmitCilInstruction(ilVar, OpCodes.Conv_R8);
                            return;

                        case SpecialType.System_Byte:
                            Context.EmitCilInstruction(ilVar, OpCodes.Conv_I1);
                            return;

                        case SpecialType.System_Int16:
                            Context.EmitCilInstruction(ilVar, OpCodes.Conv_I2);
                            return;

                        case SpecialType.System_Int32:
                            // byte/char are pushed as Int32 by the runtime 
                            if (typeInfo.Type.SpecialType != SpecialType.System_SByte && typeInfo.Type.SpecialType != SpecialType.System_Byte && typeInfo.Type.SpecialType != SpecialType.System_Char)
                                Context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
                            return;

                        case SpecialType.System_Int64:
                            var convOpCode = typeInfo.Type.SpecialType == SpecialType.System_Char || typeInfo.Type.SpecialType == SpecialType.System_Byte ? OpCodes.Conv_U8 : OpCodes.Conv_I8;
                            Context.EmitCilInstruction(ilVar, convOpCode);
                            return;

                        case SpecialType.System_Decimal:
                            var operand = typeInfo.ConvertedType.GetMembers().OfType<IMethodSymbol>().Single(m => m.MethodKind == MethodKind.Constructor && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == typeInfo.Type.SpecialType);
                            Context.EmitCilInstruction(ilVar, OpCodes.Newobj, operand.MethodResolverExpression(Context));
                            return;
                        
                        default:
                            throw new Exception($"Conversion from {typeInfo.Type} to {typeInfo.ConvertedType}  not implemented.");
                    }
                }

                if (conversion.MethodSymbol != null)
                {
                    AddMethodCall(ilVar, conversion.MethodSymbol, false);
                }
            }

            if (conversion.IsImplicit && conversion.IsBoxing)
            {
                AddCilInstruction(ilVar, OpCodes.Box, typeInfo.Type);
            }
            else if (conversion.IsIdentity && typeInfo.Type.Name == "Index" && !expression.IsKind(SyntaxKind.IndexExpression) && loadArrayIntoStack != null)
            {
                // We are indexing an array/indexer (this[]) using a System.Index variable; In this case
                // we need to convert from System.Index to *int* which is done through
                // the method System.Index::GetOffset(int32)
                loadArrayIntoStack();
            
                var indexed = Context.SemanticModel.GetTypeInfo(expression.Ancestors().OfType<ElementAccessExpressionSyntax>().Single().Expression);
                Utils.EnsureNotNull(indexed.Type, "Cannot be null.");
                if (indexed.Type.Name == "Span")
                    AddMethodCall(ilVar, ((IPropertySymbol)indexed.Type.GetMembers("Length").Single()).GetMethod);
                else
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldlen);
                
                Context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
                AddMethodCall(ilVar, (IMethodSymbol) typeInfo.Type.GetMembers().Single(m => m.Name == "GetOffset"));
            }
        }

        private Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> OperatorHandlerFor(SyntaxToken operatorToken)
        {
            if (operatorHandlers.ContainsKey(operatorToken.Kind()))
            {
                return operatorHandlers[operatorToken.Kind()];
            }

            throw new Exception($"Operator {operatorToken.ValueText} not supported yet (expression: {operatorToken.Parent})");
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
        private void HandleMethodInvocation(SyntaxNode target, SyntaxNode args, ISymbol? method = null)
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
                PushCall();
                StackallocAsArgumentFixer.Current?.MarkEndOfComputedCallTargetBlock();

                ProcessArgumentsTakingDefaultParametersIntoAccount(method, args);
                FixCallSite();
            }
        }

        private void ProcessArgumentsTakingDefaultParametersIntoAccount(ISymbol? method, SyntaxNode args)
        {
            Visit(args);
            if (method is not IMethodSymbol methodSymbol || args is not ArgumentListSyntax arguments || methodSymbol.Parameters.Length <= arguments.Arguments.Count)
                return;

            foreach (var arg in methodSymbol.Parameters.Skip(arguments.Arguments.Count))
            {
                LoadLiteralValue(ilVar, arg.Type, ArgumentValueToUseForDefaultParameter(arg, methodSymbol.Parameters, arguments.Arguments), false);
            }
        }

        private string ArgumentValueToUseForDefaultParameter(IParameterSymbol arg, ImmutableArray<IParameterSymbol> parameters, SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            var callerArgumentExpressionAttribute = arg.GetAttributes().SingleOrDefault(attr => attr.AttributeClass!.MetadataToken == Context.RoslynTypeSystem.CallerArgumentExpressionAttribute.MetadataToken);
            if (callerArgumentExpressionAttribute != null)
            {
                var expressionParameter = parameters.SingleOrDefault(p => p.Name == (string)callerArgumentExpressionAttribute.ConstructorArguments[0].Value);
                if (expressionParameter != null)
                    return $"\"{arguments[expressionParameter.Ordinal].Expression.ToFullString()}\"";
            }
            
            return arg.ExplicitDefaultValue();
        }

        private void HandleIdentifier(SimpleNameSyntax node)
        {
            using var trackIfNotPartOfTypeName = LineInformationTracker.Track(Context, node);
            var member = Context.SemanticModel.GetSymbolInfo(node);
            switch (member.Symbol.Kind)
            {
                case SymbolKind.Method:
                    ProcessMethodReference(node, member.Symbol as IMethodSymbol);
                    break;

                case SymbolKind.Parameter:
                    ProcessParameter(ilVar, node, (IParameterSymbol) member.Symbol);
                    break;

                case SymbolKind.Local:
                    ProcessLocalVariable(node, member);
                    break;

                case SymbolKind.Field:
                    ProcessField(node, member.Symbol as IFieldSymbol);
                    break;

                case SymbolKind.Property:
                    ProcessProperty(node, member.Symbol as IPropertySymbol);
                    break;
                
                default:
                    trackIfNotPartOfTypeName.Discard();
                    break;
            }
        }

        private void ProcessArrayCreation(ITypeSymbol elementType, InitializerExpressionSyntax initializer)
        {
            AddCilInstruction(ilVar, OpCodes.Newarr, elementType);

            var stelemOpCode = elementType.StelemOpCode();
            for (var i = 0; i < initializer?.Expressions.Count; i++)
            {
                Context.EmitCilInstruction(ilVar, OpCodes.Dup);
                Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, i);
                initializer.Expressions[i].Accept(this);

                var itemType = Context.GetTypeInfo(initializer.Expressions[i]);
                if (elementType.IsReferenceType && itemType.Type != null && itemType.Type.IsValueType)
                {
                    AddCilInstruction(ilVar, OpCodes.Box, itemType.Type);
                }

                Context.EmitCilInstruction(ilVar, stelemOpCode, stelemOpCode == OpCodes.Stelem_Any ? Context.TypeResolver.Resolve(elementType) : null);
            }
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
            if (node.Kind() != SyntaxKind.SimpleAssignmentExpression && nodeType.SpecialType != SpecialType.System_Void)
            {
                ctx.EmitCilInstruction(ilVar, OpCodes.Pop);
            }
        }
        
        private static void HandleModulusExpression(IVisitorContext context, string ilVar, ITypeSymbol lhs, ITypeSymbol rhs)
        {
            var l = lhs.GetMembers("op_Modulus").OfType<IMethodSymbol>().SingleOrDefault();
            var r = rhs.GetMembers("op_Modulus").OfType<IMethodSymbol>().SingleOrDefault();

            var operatorMethod = r ?? l;
            if (operatorMethod != null)
            {
                context.EmitCilInstruction(ilVar, OpCodes.Call, operatorMethod.MethodResolverExpression(context));
            }
            else
            {
                context.EmitCilInstruction(ilVar, OpCodes.Rem);
            }
        }
    }

    internal class ElementTypeSymbolResolver : SymbolVisitor<ITypeSymbol>
    {
        public static ElementTypeSymbolResolver Instance = new ElementTypeSymbolResolver();
        
        public override ITypeSymbol VisitEvent(IEventSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitField(IFieldSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitLocal(ILocalSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitMethod(IMethodSymbol symbol)
        {
            return symbol.ReturnType.Accept(this);
        }

        public override ITypeSymbol VisitProperty(IPropertySymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitParameter(IParameterSymbol symbol)
        {
            return symbol.Type.Accept(this);
        }

        public override ITypeSymbol VisitArrayType(IArrayTypeSymbol symbol)
        {
            return symbol.ElementType;
        }

        public override ITypeSymbol VisitNamedType(INamedTypeSymbol symbol)
        {
            return symbol;
        }
    }
}
