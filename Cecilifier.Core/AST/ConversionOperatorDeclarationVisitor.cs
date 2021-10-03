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
            var operatorMethodName = node.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ExplicitKeyword)
                ? "op_Explicit"
                : "op_Implicit";
                
            ProcessMethodDeclaration(
                node, 
                Context.Naming.MethodDeclaration(node),
                "operator", 
                operatorMethodName, 
                Context.GetTypeInfo(node.Type).Type,
                false,
                _ => base.VisitConversionOperatorDeclaration(node));
        }

        protected override string GetSpecificModifiers()
        {
            return "MethodAttributes.SpecialName";
        }
    }
}
