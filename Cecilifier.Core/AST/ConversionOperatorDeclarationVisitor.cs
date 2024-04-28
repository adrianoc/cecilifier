using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    class ConversionOperatorDeclarationVisitor : MethodDeclarationVisitor
    {
        public ConversionOperatorDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var operatorMethodName = node.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ExplicitKeyword)
                ? "op_Explicit"
                : "op_Implicit";

            ProcessMethodDeclaration(
                node,
                Context.Naming.MethodDeclaration(node),
                "operator",
                operatorMethodName,
                false,
                _ => base.VisitConversionOperatorDeclaration(node));
        }

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var declaredSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull();

            ProcessMethodDeclaration(
                node,
                Context.Naming.MethodDeclaration(node),
                "operator",
                declaredSymbol.Name,
                false,
                _ => base.VisitOperatorDeclaration(node));
        }

        protected override string GetSpecificModifiers() => Constants.Cecil.MethodAttributesSpecialName;
    }
}
