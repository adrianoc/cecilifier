using System;
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
                false,
                _ => base.VisitConversionOperatorDeclaration(node));
        }

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            var declaredSymbol = Context.SemanticModel.GetDeclaredSymbol(node);
            if (declaredSymbol == null)
                throw new Exception($"Failed to get declared symbol for {node}");
            
            ProcessMethodDeclaration(
                node, 
                Context.Naming.MethodDeclaration(node),
                "operator", 
                declaredSymbol.Name,
                false,
                _ => base.VisitOperatorDeclaration(node));
        }

        protected override string GetSpecificModifiers()
        {
            return "MethodAttributes.SpecialName";
        }
    }
}
