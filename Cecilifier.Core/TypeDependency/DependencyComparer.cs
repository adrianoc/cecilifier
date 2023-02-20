using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.TypeDependency;

internal class DependencyComparer : IComparer<BaseTypeDeclarationSyntax>
{
    private readonly IDictionary<BaseTypeDeclarationSyntax, IDictionary<string, int>> dependencies;
    private readonly IReadOnlyList<string> namespacesInScope;

    public DependencyComparer(IDictionary<BaseTypeDeclarationSyntax, IDictionary<string, int>> dependencies, IReadOnlyList<string> namespacesInScope)
    {
        this.dependencies = dependencies;
        this.namespacesInScope = namespacesInScope;
    }

    /// <summary>
    /// Given i) a map of a type to all types it depends on and ii) a list of namespace in scope when the dependencies from i were collectd,
    /// compares to *type declarations* (A,B) to determine the order in which those need to be processed to minimize *forward* referencing,
    /// i.e, referencing a type before its definition has been processed. 
    /// </summary>
    /// <returns>
    /// 0 if the types do not depend on each other
    /// 1 if A depends more on B than B on A, i.e, A >  B
    /// -1 if B depends more on A than A on B, i.e, B > A 
    /// </returns>
    /// <remarks>
    /// In case of cyclic references the comparison takes into account the number of references from A -> B and B -> A deeming
    /// `A` > `B` if number of # from A -> B > number of # from B -> A  
    /// </remarks>
    public int Compare(BaseTypeDeclarationSyntax x, BaseTypeDeclarationSyntax y)
    {
        var numberOfReferencesFromXToY = dependencies[x].Where(t => t.Key == y.NameFrom() || namespacesInScope.Any(ns => $"{ns}.{t.Key}" == y.NameFrom())).Select(p => p.Value).SingleOrDefault();
        var numberOfReferencesFromYToX = dependencies[y].Where(t => t.Key == x.NameFrom() || namespacesInScope.Any(ns => $"{ns}.{t.Key}" == x.NameFrom())).Select(p => p.Value).SingleOrDefault();

        return numberOfReferencesFromXToY - numberOfReferencesFromYToX;
    }
}
