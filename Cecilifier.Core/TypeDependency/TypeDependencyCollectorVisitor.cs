using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.TypeDependency;

public class TypeDependencyCollectorVisitor : CSharpSyntaxWalker
{
    private readonly CSharpCompilation compilation;
    private IDictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>> dependencies = new Dictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>>();
    private Stack<BaseTypeDeclarationSyntax> declaredTypes = new();
    private List<string> usings = new();
    
    public TypeDependencyCollectorVisitor(CSharpCompilation compilation)
    {
        this.compilation = compilation;
    }

    public IDictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>> Dependencies => dependencies;
    public IReadOnlyList<string> Usings => usings;

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        usings.Add(node.Name.ToString());
        base.VisitUsingDirective(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        using (ProcessTypeDeclaration(node)) 
            base.VisitClassDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        using(ProcessTypeDeclaration(node))
            base.VisitInterfaceDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        using(ProcessTypeDeclaration(node))
            base.VisitEnumDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        using(ProcessTypeDeclaration(node))
            base.VisitStructDeclaration(node);
    }

    public override void VisitQualifiedName(QualifiedNameSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node);
    }

    private void AddCurrentTypeDependencyIfNotTheSame(TypeSyntax type)
    {
        if (declaredTypes.Count == 0 || type == null) // type can be null for lambda parameters, for instance.
            return;
        
        var semanticModel = compilation.GetSemanticModel(type.SyntaxTree);
        if (String.Compare(declaredTypes.Peek().Identifier.Text, type.NameFrom(), StringComparison.Ordinal) != 0 && semanticModel.GetSymbolInfo(type).Symbol is ITypeSymbol { IsDefinition: true })
        {
            dependencies[declaredTypes.Peek()].Add(type);
        }
    }
    private DeclaredTypeTracker ProcessTypeDeclaration(BaseTypeDeclarationSyntax node)
    {
        var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);

        var currentTypeDependencies = new HashSet<TypeSyntax>();
        dependencies.Add(node, currentTypeDependencies);

        var basesInSameCompilation = node.BaseList?.Types.Where(t => semanticModel.GetTypeInfo(t.Type).Type?.IsDefinition == true) ?? Array.Empty<BaseTypeSyntax>();
        foreach (var b in basesInSameCompilation)
            currentTypeDependencies.Add(b.Type);

        declaredTypes.Push(node);

        return new DeclaredTypeTracker(declaredTypes);
    }

    private struct DeclaredTypeTracker : IDisposable
    {
        private Stack<BaseTypeDeclarationSyntax> toPop;

        public DeclaredTypeTracker(Stack<BaseTypeDeclarationSyntax> toPop) => this.toPop = toPop;

        public void Dispose()
        {
            if (toPop != null)
            {
                toPop.Pop();
                toPop = null;
            }
        }
    }
}
