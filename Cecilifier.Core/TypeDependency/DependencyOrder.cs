using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.TypeDependency;

public class DependencyOrder
{
    internal IReadOnlyList<BaseTypeDeclarationSyntax> Dependencies { get; }

    public DependencyOrder(List<BaseTypeDeclarationSyntax> dependencies) => Dependencies = dependencies;

    public override string ToString()
    {
        return string.Join(',', Dependencies.Select(d => d.Identifier.Text));
    }
}
