using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class ExpressionVisitor : SyntaxWalkerBase
    {
        private static readonly IDictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>> operatorHandlers =
            new Dictionary<SyntaxKind, Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol>>();

        private readonly string ilVar;
        private readonly Stack<LinkedListNode<string>> callFixList = new Stack<LinkedListNode<string>>();

        private bool valueTypeNoArgObjCreation;

        static ExpressionVisitor()
        {
            //TODO: Use AddCilInstruction instead.
            operatorHandlers[SyntaxKind.PlusToken] = (ctx, ilVar, left, right) =>
            {
                if (left.SpecialType == SpecialType.System_String)
                {
                    var concatArgType = right.SpecialType == SpecialType.System_String ? "string" : "object";
                    WriteCecilExpression(ctx,
                        $"{ilVar}.Append({ilVar}.Create({OpCodes.Call.ConstantName()}, assembly.MainModule.Import(typeof(string).GetMethod(\"Concat\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] {{ typeof({concatArgType}), typeof({concatArgType}) }}, null))));");
                }
                else
                {
                    WriteCecilExpression(ctx, @"{0}.Append({0}.Create({1}));", ilVar, OpCodes.Add.ConstantName());
                }
            };

            operatorHandlers[SyntaxKind.SlashToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Div.ConstantName()}));");
            operatorHandlers[SyntaxKind.GreaterThanToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Cgt.ConstantName()}));");
            operatorHandlers[SyntaxKind.EqualsEqualsToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Ceq.ConstantName()}));");
            operatorHandlers[SyntaxKind.LessThanToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Clt.ConstantName()}));");
            operatorHandlers[SyntaxKind.MinusToken] = (ctx, ilVar, left, right) => WriteCecilExpression(ctx, $"{ilVar}.Append({ilVar}.Create({OpCodes.Sub.ConstantName()}));");
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

            return ev.valueTypeNoArgObjCreation;
        }

        public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            if (node.Initializer == null)
            {
                node.Type.RankSpecifiers[0].Accept(this);
            }
            else
            {
                AddCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);
            }

            var elementTypeInfo = Context.GetTypeInfo(node.Type.ElementType);
            ProcessArrayCreation(elementTypeInfo.Type, node.Initializer);
        }

        public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            var arrayType = Context.GetTypeInfo(node);
            if (arrayType.Type == null)
            {
                throw new Exception($"Unable to infer array type: {node}");
            }

            AddCilInstruction(ilVar, OpCodes.Ldc_I4, node.Initializer.Expressions.Count);

            var arrayTypeSymbol = (IArrayTypeSymbol) arrayType.Type;
            ProcessArrayCreation(arrayTypeSymbol.ElementType, node.Initializer);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitTupleExpression(TupleExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitRefExpression(RefExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            base.VisitEqualsValueClause(node);
            InjectRequiredConversions(node.Value);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var leftNodeMae = node.Left as MemberAccessExpressionSyntax;
            CSharpSyntaxNode exp = leftNodeMae != null ? leftNodeMae.Name : node.Left;
            if (Context.SemanticModel.GetSymbolInfo(exp).Symbol?.Kind == SymbolKind.Property) // check if the left hand side of the assignment is a property and handle that as a method (set) call.
            {
                HandleMethodInvocation(node.Left, node.Right);
                return;
            }

            var visitor = new AssignmentVisitor(Context, ilVar, node);
            
            visitor.InstrutionPreceedingValueToLoad = Context.CurrentLine;
            Visit(node.Right);
            if (!valueTypeNoArgObjCreation)
            {
                visitor.Visit(node.Left);
            }
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
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

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.NullLiteralExpression:
                    AddCilInstruction(ilVar, OpCodes.Ldnull);
                    break;

                case SyntaxKind.StringLiteralExpression:
                    AddCilInstruction(ilVar, OpCodes.Ldstr, node.ToFullString());
                    break;

                case SyntaxKind.CharacterLiteralExpression:
                case SyntaxKind.NumericLiteralExpression:
                    AddLocalVariableAndHandleCallOnValueTypeLiterals(node, "assembly.MainModule.TypeSystem.Int32", node.ToString());
                    break;

                case SyntaxKind.TrueLiteralExpression:
                case SyntaxKind.FalseLiteralExpression:
                    AddLocalVariableAndHandleCallOnValueTypeLiterals(node, "assembly.MainModule.TypeSystem.Boolean", bool.Parse(node.ToString()) ? 1 : 0);
                    break;

                default:
                    throw new ArgumentException($"Literal ( {node}) of type {node.Kind()} not supported yet.");
            }

            void AddLocalVariableAndHandleCallOnValueTypeLiterals(LiteralExpressionSyntax literalNode, string cecilTypeSystemReference, object literalValue)
            {
                AddCilInstruction(ilVar, LoadOpCodeFor(literalNode), literalValue);
                var localVarParent = (CSharpSyntaxNode) literalNode.Parent;
                if (localVarParent.Accept(new UsageVisitor()) == UsageKind.CallTarget)
                {
                    var tempLocalName = MethodExtensions.LocalVariableNameFor("tmp_", "tmp_".UniqueId().ToString());
                    AddCecilExpression("var {0} = new VariableDefinition({1});", tempLocalName, cecilTypeSystemReference);
                    AddCecilExpression("{0}.Body.Variables.Add({1});", Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName, tempLocalName);

                    AddCilInstruction(ilVar, OpCodes.Stloc, $"{tempLocalName}");
                    AddCilInstruction(ilVar, OpCodes.Ldloca_S, $"{tempLocalName}");
                }
            }
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            HandleMethodInvocation(node.Expression, node.ArgumentList);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            string Emit(OpCode op)
            {
                var instVarName = op.Name + op.Name.UniqueId();
                AddCecilExpression(@"var {0} = {1}.Create({2});", instVarName, ilVar, op.ConstantName());

                return instVarName;
            }

            var conditionEnd = Emit(OpCodes.Nop);
            var whenFalse = Emit(OpCodes.Nop);

            Visit(node.Condition);
            AddCilInstruction(ilVar, OpCodes.Brfalse_S, whenFalse);

            Visit(node.WhenTrue);
            AddCilInstruction(ilVar, OpCodes.Br_S, conditionEnd);

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
            base.VisitArgument(node);
            InjectRequiredConversions(node.Expression);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax exp)
        {
            Visit(exp.Expression);
            Visit(exp.Name);
        }

        public override void VisitThisExpression(ThisExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            if (node.OperatorToken.Kind() == SyntaxKind.AmpersandToken)
            {
                Visit(node.Operand);
            }
            else
            {
                LogUnsupportedSyntax(node);
            }
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            base.VisitParenthesizedExpression(node);

            var localVarParent = (CSharpSyntaxNode) node.Parent;
            if (localVarParent.Accept(new UsageVisitor()) == UsageKind.CallTarget)
            {
                var tempLocalName = MethodExtensions.LocalVariableNameFor("tmp_", "tmp_".UniqueId().ToString());
                AddCecilExpression("var {0} = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);", tempLocalName);
                AddCecilExpression("{0}.Body.Variables.Add({1});", Context.DefinitionVariables.GetLastOf(MemberKind.Method).VariableName, tempLocalName);

                AddCilInstruction(ilVar, OpCodes.Stloc, tempLocalName);
                AddCilInstruction(ilVar, OpCodes.Ldloca_S, tempLocalName);
            }
        }

        public override void VisitMakeRefExpression(MakeRefExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitRefTypeExpression(RefTypeExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitRefValueExpression(RefValueExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitCheckedExpression(CheckedExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitDefaultExpression(DefaultExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitSizeOfExpression(SizeOfExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitInitializerExpression(InitializerExpressionSyntax node)
        {
            LogUnsupportedSyntax(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            //TODO: Refactor to reuse code from VisitIdentifierName....
            var ctorInfo = Context.SemanticModel.GetSymbolInfo(node);

            var method = (IMethodSymbol) ctorInfo.Symbol;
            if (TryProcessNoArgsCtorInvocationOnValueType(node, method, ctorInfo))
            {
                return;
            }

            EnsureMethodAvailable(method, Array.Empty<TypeParameterSyntax>());

            AddCilInstruction(ilVar, OpCodes.Newobj, method.MethodResolverExpression(Context));
            PushCall();

            Visit(node.ArgumentList);
            FixCallSite();
        }

        private bool TryProcessNoArgsCtorInvocationOnValueType(ObjectCreationExpressionSyntax node, IMethodSymbol methodSymbol, SymbolInfo ctorInfo)
        {
            if (ctorInfo.Symbol.ContainingType.IsReferenceType || methodSymbol.Parameters.Length > 0)
            {
                return false;
            }

            new ValueTypeNoArgCtorInvocationVisitor(Context, ilVar, ctorInfo).Visit(node.Parent);
            return valueTypeNoArgObjCreation = true;
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            Context.WriteCecilExpression($"\r\n// {node}\r\n");
            base.Visit(node.Expression);

            var info = Context.GetTypeInfo(node.Expression);
            if (node.Expression.Kind() != SyntaxKind.SimpleAssignmentExpression && info.Type.SpecialType != SpecialType.System_Void)
            {
                AddCilInstruction(ilVar, OpCodes.Pop);
            }
        }

        private OpCode LoadOpCodeFor(LiteralExpressionSyntax node)
        {
            var info = Context.SemanticModel.GetTypeInfo(node);
            switch (info.Type.SpecialType)
            {
                case SpecialType.System_Single:
                    return OpCodes.Ldc_R4;

                case SpecialType.System_Double:
                    return OpCodes.Ldc_R8;

                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                    return OpCodes.Ldc_I4;

                case SpecialType.System_Int64:
                    return OpCodes.Ldc_I8;

                case SpecialType.System_Char:
                    return OpCodes.Ldc_I4;

                case SpecialType.System_Boolean:
                    return OpCodes.Ldc_I4;
            }

            throw new ArgumentException(string.Format("Literal type {0} not supported.", info.Type), "node");
        }

        private OpCode StelemOpCodeFor(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte: return OpCodes.Stelem_I1;
                case SpecialType.System_Int16: return OpCodes.Stelem_I2;
                case SpecialType.System_Int32: return OpCodes.Stelem_I4;
                case SpecialType.System_Int64: return OpCodes.Stelem_I8;
                case SpecialType.System_Single: return OpCodes.Stelem_R4;
                case SpecialType.System_Double: return OpCodes.Stelem_R8;

                case SpecialType.None: // custom types.
                case SpecialType.System_String:
                case SpecialType.System_Object: return OpCodes.Stelem_Ref;
            }

            throw new Exception($"Element type {type.Name} not supported.");
        }

        /*
         * Support for scenario in which a method is being referenced before it has been declared. This can happen for instance in code like:
         *
         * class C
         * {
         *     void Foo() { Bar(); }
         *     void Bar() {}
         * }
         *
         * In this case when the first reference to Bar() is found (in method Foo()) the method itself has not been defined yet.
         */
        private void EnsureMethodAvailable(IMethodSymbol method, TypeParameterSyntax[] typeParameters)
        {
            if (!method.IsDefinedInCurrentType(Context))
            {
                return;
            }

            var varName = method.LocalVariableName();
            if (Context.Contains(varName))
            {
                return;
            }

            //TODO: Try to reuse SyntaxWalkerBase.ResolveType(TypeSyntax)
            var returnType = Context.TypeResolver.ResolveTypeLocalVariable(method.ReturnType.Name) ?? Context.TypeResolver.ResolvePredefinedType(method.ReturnType);
            MethodDeclarationVisitor.AddMethodDefinition(Context, varName, method.Name, "MethodAttributes.Private", returnType, typeParameters);
            Context.DefinitionVariables.RegisterMethod(method.ContainingType.Name, method.Name, method.Parameters.Select(p => p.Type.Name).ToArray(), varName);
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
            var parentMae = node.Parent as MemberAccessExpressionSyntax;
            var isAccessOnThisOrObjectCreation = true;
            if (parentMae != null)
            {
                isAccessOnThisOrObjectCreation = parentMae.Expression.IsKind(SyntaxKind.ObjectCreationExpression);
            }

            var parentExp = node.Parent;
            if (!parentExp.IsKind(SyntaxKind.SimpleMemberAccessExpression)) // if this is an *unqualified* access we need to load *this*
            {
                AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            if (parentExp.Kind() == SyntaxKind.SimpleAssignmentExpression || parentMae != null && parentMae.Name.Identifier == node.Identifier && parentMae.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                AddMethodCall(ilVar, propertySymbol.SetMethod, isAccessOnThisOrObjectCreation);
            }
            else
            {
                AddMethodCall(ilVar, propertySymbol.GetMethod, isAccessOnThisOrObjectCreation);
            }
        }

        private void ProcessField(SimpleNameSyntax node, IFieldSymbol fieldSymbol)
        {
            if (fieldSymbol.IsStatic && fieldSymbol.IsDefinedInCurrentType(Context))
            {
                throw new Exception("Static field handling not implemented yet");
            }

            AddCilInstruction(ilVar, OpCodes.Ldarg_0);

            if (HandleLoadAddress(ilVar, fieldSymbol.Type, (CSharpSyntaxNode) node.Parent, OpCodes.Ldflda, fieldSymbol.Name, MemberKind.Field, fieldSymbol.ContainingType.Name))
            {
                return;
            }
            AddCilInstruction(ilVar, OpCodes.Ldfld, Context.DefinitionVariables.GetVariable(fieldSymbol.Name, MemberKind.Field, fieldSymbol.ContainingType.Name).VariableName);
        }

        private void ProcessLocalVariable(SimpleNameSyntax localVar, SymbolInfo varInfo)
        {
            var symbol = (ILocalSymbol) varInfo.Symbol;
            var localVarParent = (CSharpSyntaxNode) localVar.Parent;
            if (HandleLoadAddress(ilVar, symbol.Type, localVarParent, OpCodes.Ldloca, symbol.Name, MemberKind.LocalVariable))
            {
                return;
            }

            AddCilInstruction(ilVar, OpCodes.Ldloc, Context.DefinitionVariables.GetVariable(symbol.Name, MemberKind.LocalVariable).VariableName);
            HandlePotentialDelegateInvocationOn(localVar, symbol.Type, ilVar);
            HandlePotentialFixedLoad(localVar, symbol);
        }

        private void HandlePotentialFixedLoad(SyntaxNode localVar, ILocalSymbol symbol)
        {
            if (!symbol.IsFixed)
                return;

            AddCilInstruction(ilVar, OpCodes.Conv_U);
        }

        private void ProcessMethodCall(SimpleNameSyntax node, IMethodSymbol method)
        {
            if (!method.IsStatic && method.IsDefinedInCurrentType(Context) && node.Parent.Kind() == SyntaxKind.InvocationExpression)
            {
                AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            }

            //TODO: We need to find the InvocationSyntax that node represents...
            EnsureMethodAvailable(method.OverriddenMethod ?? method.OriginalDefinition, Array.Empty<TypeParameterSyntax>());
            var isAccessOnThis = !node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression);

            var mae = node.Parent as MemberAccessExpressionSyntax;
            if (mae?.Expression.IsKind(SyntaxKind.ObjectCreationExpression) == true)
            {
                isAccessOnThis = true;
            }

            AddMethodCall(ilVar, method, isAccessOnThis);
        }

        private void InjectRequiredConversions(ExpressionSyntax expression)
        {
            var typeInfo = Context.SemanticModel.GetTypeInfo(expression);

            var conversion = Context.SemanticModel.GetConversion(expression);
            //TODO: 
            if (conversion.IsImplicit && conversion.IsNumeric)
            {
                switch (typeInfo.ConvertedType.SpecialType)
                {
                    case SpecialType.System_Single:
                        AddCilInstruction(ilVar, OpCodes.Conv_R4);
                        return;
                    case SpecialType.System_Double:
                        AddCilInstruction(ilVar, OpCodes.Conv_R8);
                        return;

                    case SpecialType.System_Byte:
                        AddCilInstruction(ilVar, OpCodes.Conv_I1);
                        return;
                    case SpecialType.System_Int16:
                        AddCilInstruction(ilVar, OpCodes.Conv_I2);
                        return;
                    case SpecialType.System_Int32:
                        AddCilInstruction(ilVar, OpCodes.Conv_I4);
                        return;
                    case SpecialType.System_Int64:
                        AddCilInstruction(ilVar, OpCodes.Conv_I8);
                        return;

                    default:
                        throw new Exception(string.Format("Conversion from {0} to {1}  not implemented.", typeInfo.Type, typeInfo.ConvertedType));
                }
            }

            if (conversion.IsImplicit && conversion.IsBoxing)
            {
                AddCilInstruction(ilVar, OpCodes.Box, typeInfo.Type);
            }
        }

        private Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> OperatorHandlerFor(SyntaxToken operatorToken)
        {
            if (operatorHandlers.ContainsKey(operatorToken.Kind()))
            {
                return operatorHandlers[operatorToken.Kind()];
            }

            throw new Exception(string.Format("Operator {0} not supported yet (expression: {1})", operatorToken.ValueText, operatorToken.Parent));
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
        private void HandleMethodInvocation(SyntaxNode target, SyntaxNode args)
        {
            Visit(target);
            PushCall();

            Visit(args);
            FixCallSite();
        }

        private void HandleIdentifier(SimpleNameSyntax node)
        {
            var member = Context.SemanticModel.GetSymbolInfo(node);
            switch (member.Symbol.Kind)
            {
                case SymbolKind.Method:
                    ProcessMethodCall(node, member.Symbol as IMethodSymbol);
                    break;

                case SymbolKind.Parameter:
                    ProcessParameter(ilVar, node, member.Symbol as IParameterSymbol);
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
            }
        }

        private void ProcessArrayCreation(ITypeSymbol elementType, InitializerExpressionSyntax initializer)
        {
            AddCilInstruction(ilVar, OpCodes.Newarr, Context.TypeResolver.Resolve(elementType));

            var stelemOpCode = StelemOpCodeFor(elementType);
            for (var i = 0; i < initializer?.Expressions.Count; i++)
            {
                AddCilInstruction(ilVar, OpCodes.Dup);
                AddCilInstruction(ilVar, OpCodes.Ldc_I4, i);
                initializer.Expressions[i].Accept(this);

                var itemType = Context.GetTypeInfo(initializer.Expressions[i]);
                if (elementType.IsReferenceType && itemType.Type != null && itemType.Type.IsValueType)
                {
                    AddCilInstruction(ilVar, OpCodes.Box, Context.TypeResolver.Resolve(itemType.Type));
                }

                AddCilInstruction(ilVar, stelemOpCode);
            }
        }
    }
}
