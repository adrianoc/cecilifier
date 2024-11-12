using System;
using System.Collections.Generic;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.TypeDependency;

public class TypeDependencyCollectorVisitor : CSharpSyntaxWalker
{
    private IDictionary<BaseTypeDeclarationSyntax, IDictionary<string, int>> dependencies = new Dictionary<BaseTypeDeclarationSyntax, IDictionary<string, int>>();
    private Stack<BaseTypeDeclarationSyntax> declaredTypes = new();
    private List<string> usings = new();

    public IDictionary<BaseTypeDeclarationSyntax, IDictionary<string, int>> Dependencies => dependencies;
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
        using (ProcessTypeDeclaration(node))
            base.VisitInterfaceDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        using (ProcessTypeDeclaration(node))
            base.VisitEnumDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        using (ProcessTypeDeclaration(node))
            base.VisitStructDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        using (ProcessTypeDeclaration(node))
            base.VisitRecordDeclaration(node);
    }

    public override void VisitQualifiedName(QualifiedNameSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node);
        if (node.Right.IsKind(SyntaxKind.GenericName))
            node.Right.Accept(this);
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node);
        base.VisitGenericName(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node);
    }

    public override void VisitTypeArgumentList(TypeArgumentListSyntax node)
    {
        foreach (var typeArgument in node.Arguments)
        {
            AddCurrentTypeDependencyIfNotTheSame(typeArgument);
        }
    }

    private void AddCurrentTypeDependencyIfNotTheSame(TypeSyntax type)
    {
        if (declaredTypes.Count == 0 || type == null || type.IsKind(SyntaxKind.OmittedTypeArgument)) // type can be null for lambda parameters, for instance.
            return;

        if (String.Compare(declaredTypes.Peek().Identifier.Text, type.NameFrom(), StringComparison.Ordinal) != 0)
        {
            var foundDependencies = dependencies[declaredTypes.Peek()];
            if (!foundDependencies.TryGetValue(type.ToString(), out var referenceCounter))
            {
                referenceCounter = 0; // this is the first reference found from `current type` -> type, set to 0, will increment below.
            }
            foundDependencies[type.NameFrom(expandAttributeName:true)] = ++referenceCounter;
        }
    }
    private DeclaredTypeTracker ProcessTypeDeclaration(BaseTypeDeclarationSyntax node)
    {
        var currentTypeDependencies = new Dictionary<string, int>();
        dependencies.Add(node, currentTypeDependencies);
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
