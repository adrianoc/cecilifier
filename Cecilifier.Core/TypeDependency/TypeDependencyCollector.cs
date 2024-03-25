using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.TypeDependency;

public class TypeDependencyCollector
{
    public TypeDependencyCollector(CSharpCompilation comp)
    {
        var visitor = new TypeDependencyCollectorVisitor();
        foreach (var st in comp.SyntaxTrees)
            visitor.Visit(st.GetRoot());

        Unordered = new DependencyOrder(new List<BaseTypeDeclarationSyntax>(visitor.Dependencies.Keys));
        Ordered = SortByDependency(visitor.Dependencies, visitor.Usings);
    }

    private DependencyOrder SortByDependency(IDictionary<BaseTypeDeclarationSyntax, IDictionary<string, int>> dependencies, IReadOnlyList<string> usings)
    {
        var sortedDependency = new List<BaseTypeDeclarationSyntax>(dependencies.Keys);
        sortedDependency.Sort(new DependencyComparer(dependencies, usings));

        return new DependencyOrder(sortedDependency);
    }

    public DependencyOrder Unordered { get; }
    public DependencyOrder Ordered { get; }
}
