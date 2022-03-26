using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class CompilationUnitVisitor : SyntaxWalkerBase
    {
        internal CompilationUnitVisitor(IVisitorContext ctx) : base(ctx)
        {
        }

        public BaseTypeDeclarationSyntax MainType => mainType;
        public string MainMethodDefinitionVariable { get; private set; }

        public override void VisitCompilationUnit(CompilationUnitSyntax node)
        {
            HandleAttributesInMemberDeclaration(node.AttributeLists, "assembly");
            base.VisitCompilationUnit(node);
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            try
            {
                Context.CurrentNamespace = NamespaceOf(node);
                base.VisitNamespaceDeclaration(node);
            }
            finally
            {
                Context.CurrentNamespace = string.Empty;
            }

            string NamespaceOf(NamespaceDeclarationSyntax namespaceDeclarationSyntax)
            {
                var namespaceHierarchy = namespaceDeclarationSyntax.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>().Reverse();
                return string.Join('.', namespaceHierarchy.Select(curr => curr.Name.WithoutTrivia()));
            }
        }
     
        public override void VisitGlobalStatement(GlobalStatementSyntax node)
        {
            if (_globalStatementHandler == null)
            {
                Context.WriteComment("Begin of global statements.");
                _globalStatementHandler = new GlobalStatementHandler(Context, node);
            }

            if (_globalStatementHandler.HandleGlobalStatement(node))
            {
                Context.WriteNewLine();
                Context.WriteComment("End of global statements.");
                _globalStatementHandler = null;
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
            UpdateTypeInformation(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        private void UpdateTypeInformation(BaseTypeDeclarationSyntax node)
        {
            if (mainType == null)
                mainType = node;

            var typeSymbol = Context.SemanticModel.GetDeclaredSymbol(node) as ITypeSymbol;
            if (typeSymbol == null)
                return;

            if (MainMethodDefinitionVariable == null)
            {
                var mainMethod = (IMethodSymbol) typeSymbol.GetMembers().SingleOrDefault(m => m is IMethodSymbol {IsStatic: true, Name: "Main", ReturnsVoid: true});
                if (mainMethod != null)
                    MainMethodDefinitionVariable = Context.DefinitionVariables.GetMethodVariable(mainMethod.AsMethodDefinitionVariable());
            }

            var mainTypeSymbol = (ITypeSymbol) Context.SemanticModel.GetDeclaredSymbol(mainType);
            if (typeSymbol.GetMembers().Length > mainTypeSymbol?.GetMembers().Length)
            {
                mainType = node;
            }
        }

        private BaseTypeDeclarationSyntax mainType;
        private GlobalStatementHandler _globalStatementHandler;
    }
}
