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
            base.VisitCompilationUnit(node);
            NewMethod();
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
        
        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        private void NewMethod()
        {
            var collectedTypes = new TypeDependency.TypeDependencyCollector((CSharpCompilation)Context.SemanticModel.Compilation);
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
            if (mainType == null)
                mainType = node;

            var typeSymbol = ModelExtensions.GetDeclaredSymbol(Context.SemanticModel, node) as ITypeSymbol;
            if (typeSymbol == null)
                return;

            if (MainMethodDefinitionVariable == null)
            {
                var mainMethod = (IMethodSymbol) typeSymbol.GetMembers().SingleOrDefault(m => m is IMethodSymbol {IsStatic: true, Name: "Main", ReturnsVoid: true});
                if (mainMethod != null)
                    MainMethodDefinitionVariable = Context.DefinitionVariables.GetMethodVariable(mainMethod.AsMethodDefinitionVariable());
            }

            var mainTypeSymbol = (ITypeSymbol) ModelExtensions.GetDeclaredSymbol(Context.SemanticModel, mainType);
            if (typeSymbol.GetMembers().Length > mainTypeSymbol?.GetMembers().Length)
            {
                mainType = node;
            }
        }

        private BaseTypeDeclarationSyntax mainType;
        private GlobalStatementHandler _globalStatementHandler;
    }
}
