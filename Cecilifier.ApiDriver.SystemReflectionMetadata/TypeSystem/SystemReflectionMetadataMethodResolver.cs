using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataMethodResolver : IMethodResolver
{
    public string Resolve(IMethodSymbol method, IVisitorContext context)
    {
        throw new NotImplementedException();
    }
}
