using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.TypeDependency;

internal class DependencyComparer : IComparer<BaseTypeDeclarationSyntax>
{
    private readonly IDictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>> dependencies;
    private readonly IReadOnlyList<string> namespacesInScope;

    public DependencyComparer(IDictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>> dependencies, IReadOnlyList<string> namespacesInScope)
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
    /// 0 if the types do not depend on each other if there's a cyclic dependency
    /// 1 if A depends on B, i.e, A >  B
    /// -1 if B depends on A, i.e, B > A  
    /// </returns>
    public int Compare(BaseTypeDeclarationSyntax x, BaseTypeDeclarationSyntax y)
    {
        var xDependsOnY = dependencies[x].Any(t => t.NameFrom() == y.NameFrom() || namespacesInScope.Any(ns => $"{ns}.{t.NameFrom()}" == y.NameFrom()));
        var yDependsOnX = dependencies[y].Any(t => t.NameFrom() == x.NameFrom()|| namespacesInScope.Any(ns => $"{ns}.{t.NameFrom()}" == x.NameFrom()));
        
        if (xDependsOnY && !yDependsOnX)
            return 1; // x depends on y so y needs to appear first, i.e, x > y
        
        if (yDependsOnX && !xDependsOnY)
            return -1; // y depends on x so x needs to appear first, i.e, x < y

        if (xDependsOnY && yDependsOnX)
            return 0;
        
        return 0;
    }
}
