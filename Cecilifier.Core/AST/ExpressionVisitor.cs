using System;
using System.Reflection;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
    class ExpressionVisitor : SyntaxWalkerBase
    {
        private readonly IMemoryLocationResolver resolver;

        internal ExpressionVisitor(IVisitorContext ctx, IMemoryLocationResolver resolver) : base(ctx)
        {
            this.resolver = resolver;
        }

        protected override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
        }

        protected override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
        }

        protected override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            //new SyntaxTreeDump("IE", node);
            //return;
            Console.WriteLine("IE Exp: {0}", node.Expression);
            Console.WriteLine("IE Args: {0}", node.ArgumentList);

            // n.DoIt(10 + x);
            // push n
            // push 10
            // push x
            // add
            // call Doit(Int32)
            Visit(node.Expression);

            //var info = Context.GetSemanticInfo(node.Expression);
            //base.VisitInvocationExpression(node);
        }

        protected override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
        }

        protected override void VisitMemberAccessExpression(MemberAccessExpressionSyntax exp)
        {
            var methodVar = Context.CurrentLocalVariable;
            var ilVar = Context[MethodDeclarationVisitor.IlVar];

            var expInfo = Context.SemanticModel.GetSemanticInfo(exp.Expression);
            if (expInfo.IsCompileTimeConstant)
            {
                AddCilInstruction(methodVar.VarName, ilVar, "OpCodes.Ldc_I4", expInfo.ConstantValue);
                //var ordinal = resolver.NextLocalVarOrdinal();
                // TODO: if exp.Name (method) expects INT -> Stloc x, Ldloca x
                // TODO: otherwise: Box
            }
            else if (expInfo.Symbol != null)
            {
                switch(expInfo.Symbol.Kind)
                {
                    case SymbolKind.Parameter:
                        var p = (ParameterSymbol) expInfo.Symbol ;
                        //AddCecilExpression("il.Append(il.Create(OpCodes.Ldarga_S, {0}));", p.Ordinal + (p.ContainingSymbol.IsStatic ? 0 : 1));
                        AddCilInstruction(methodVar.VarName, ilVar, "OpCodes.Ldarga_S", methodVar.VarName + ".Parameters[" + p.Ordinal + "]");
                        //TODO: ldarga only if exp.Name (method) is declared on Int32
                        //TODO: otherwise should be ldarg, box
                        break;

                    case SymbolKind.Local:
                        var l = (LocalSymbol) expInfo.Symbol ;
                        //AddCecilExpression("il.Append(il.Create(OpCodes.Ldloca_S, {0}));", 1);
                        AddCilInstruction(methodVar.VarName, ilVar, "OpCodes.Ldloca_S", 1);
                        break;

                    case SymbolKind.Method:
                        var m = (MethodSymbol) expInfo.Symbol;
                        break;

                    default:
                        Console.WriteLine(" $$$$ => [{1}] {0}", expInfo.Symbol.Kind, expInfo.Symbol.Name);
                        break;
                }
            }
            else
            {
                Console.WriteLine("\r\nWFT: {0}", expInfo);
            }

            var member = Context.SemanticModel.GetSemanticInfo(exp.Name);
            var method = member.Symbol as MethodSymbol;
            if (method != null)
            {
                var declaringTypeName = method.ContainingType.ToDisplayString(new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));

                AddCilInstruction(methodVar.VarName, ilVar, "OpCodes.Call", String.Format("assembly.MainModule.Import(ResolveMethod(\"{0}\", \"{1}\", \"{2}\"{3}))",
                                            method.ContainingAssembly.AssemblyName.FullName, 
                                            declaringTypeName, 
                                            method.Name, 
                                            method.Parameters.Aggregate("", (acc, curr) => ", \"" + curr.Name + "\"")));

            }
        }

        protected override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
        }

        protected override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
        }

        protected override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
        }

        protected override void VisitMakeRefExpression(MakeRefExpressionSyntax node)
        {
        }

        protected override void VisitRefTypeExpression(RefTypeExpressionSyntax node)
        {
        }

        protected override void VisitRefValueExpression(RefValueExpressionSyntax node)
        {
        }

        protected override void VisitCheckedExpression(CheckedExpressionSyntax node)
        {
        }

        protected override void VisitDefaultExpression(DefaultExpressionSyntax node)
        {
        }

        protected override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
        }

        protected override void VisitSizeOfExpression(SizeOfExpressionSyntax node)
        {
        }

        protected override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
        }

        protected override void VisitCastExpression(CastExpressionSyntax node)
        {
        }

        protected override void VisitInitializerExpression(InitializerExpressionSyntax node)
        {
        }

        protected override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
        }

        protected override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
        }

        // TypeSyntax ?
        // InstanceExpressionSyntax ?

        // 
        // AnonymousMethodExpressionSyntax
        // SimpleLambdaExpressionSyntax
        // ParenthesizedLambdaExpressionSyntax
        // 
        // 
        // AnonymousObjectCreationExpressionSyntax
        // ArrayCreationExpressionSyntax
        // ImplicitArrayCreationExpressionSyntax
        // StackAllocArrayCreationExpressionSyntax
        // QueryExpressionSyntax
    }
}
