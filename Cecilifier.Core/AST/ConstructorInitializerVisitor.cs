using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.Extensions;

namespace Cecilifier.Core.AST
{
    internal class ConstructorInitializerVisitor : SyntaxWalkerBase
    {
        private readonly string ilVar;

        internal ConstructorInitializerVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
            this.ilVar = ilVar;
        }

        public override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            base.VisitConstructorInitializer(node);

            var info = Context.SemanticModel.GetSymbolInfo(node);
            var targetCtor = (IMethodSymbol) info.Symbol;

            string operand = targetCtor.MethodResolverExpression(Context);
            Context.EmitCilInstruction(ilVar, OpCodes.Call, operand);

            var declaringType = (BaseTypeDeclarationSyntax) node.Parent.Parent;

            //FIXME: Fix ctor construction
            //
            // 1. Field initialization
            // 2. Ctor initializer
            //    2.1 Load parameters
            //    2.2 Call base/this ctor
            // 3. If no ctor initializer call base ctor
            // 4. Ctor body
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            ExpressionVisitor.Visit(Context, ilVar, node.Expression);
        }
    }
}
