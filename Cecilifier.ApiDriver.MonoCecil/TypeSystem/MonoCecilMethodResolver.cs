using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilMethodResolver : IMethodResolver
{
    public string Resolve(IMethodSymbol method, IVisitorContext context)
    {
        throw new NotImplementedException();
    }
}
