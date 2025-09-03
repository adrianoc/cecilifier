using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

internal class SystemReflectionMetadataIlContext : IlContext
{
    public SystemReflectionMetadataIlContext(string variableName, string relatedMethodVar) : base(variableName, relatedMethodVar)
    {
    }
}
