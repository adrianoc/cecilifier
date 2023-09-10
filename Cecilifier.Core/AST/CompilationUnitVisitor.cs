using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            VisitDelegates();
            VisitDeclaredTypesSortedByDependencies();
            
            base.VisitCompilationUnit(node);
        }

        public override void VisitGlobalStatement(GlobalStatementSyntax node)
        {
            if (_globalStatementHandler == null)
            {
                Context.WriteComment("Begin of global statements.");
                _globalStatementHandler = new GlobalStatementHandler(Context, node);
                MainMethodDefinitionVariable = _globalStatementHandler.MainMethodDefinitionVariable;
            }

            if (_globalStatementHandler.HandleGlobalStatement(node))
            {
                Context.WriteNewLine();
                Context.WriteComment("End of global statements.");
                _globalStatementHandler = null;
            }
        }

        private void VisitDelegates()
        {
            var compilation = (CSharpCompilation) Context.SemanticModel.Compilation;
            var ds = compilation.SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<DelegateDeclarationSyntax>());
            foreach(var delegateDeclaration in ds)
                new TypeDeclarationVisitor(Context).Visit(delegateDeclaration);
        }

        private void VisitDeclaredTypesSortedByDependencies()
        {
            var collectedTypes = new TypeDependency.TypeDependencyCollector((CSharpCompilation) Context.SemanticModel.Compilation);
            foreach (var typeDeclaration in collectedTypes.Ordered.Dependencies)
            {
                if (Context.SemanticModel.GetDeclaredSymbol(typeDeclaration).ContainingType != null)
                    continue;

                new TypeDeclarationVisitor(Context).Visit(typeDeclaration);
                UpdateTypeInformation(typeDeclaration);
            }
        }

        private void UpdateTypeInformation(BaseTypeDeclarationSyntax node)
        {
            var typeSymbol = ModelExtensions.GetDeclaredSymbol(Context.SemanticModel, node) as ITypeSymbol;
            if (typeSymbol == null)
                return;

            if (MainMethodDefinitionVariable == null)
            {
                var mainMethod = (IMethodSymbol) typeSymbol.GetMembers().SingleOrDefault(m => m is IMethodSymbol { IsStatic: true, Name: "Main", ReturnsVoid: true });
                if (mainMethod != null)
                    MainMethodDefinitionVariable = Context.DefinitionVariables.GetMethodVariable(mainMethod.AsMethodDefinitionVariable());
            }

            var mainTypeSymbol = (ITypeSymbol) Context.SemanticModel.GetDeclaredSymbol(mainType ?? node);
            if (mainType == null || typeSymbol.GetMembers().Length > mainTypeSymbol?.GetMembers().Length)
            {
                mainType = node;
            }
        }

        private BaseTypeDeclarationSyntax mainType;
        private GlobalStatementHandler _globalStatementHandler;
    }
}
