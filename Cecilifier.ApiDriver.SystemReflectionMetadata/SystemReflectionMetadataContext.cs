using Cecilifier.Core;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

public class SystemReflectionMetadataContext : CecilifierContextBase, IVisitorContext
{
    public SystemReflectionMetadataContext(CecilifierOptions options, SemanticModel semanticModel, byte indentation = 3) : base(options, semanticModel, indentation)
    {
        ApiDriver = new SystemReflectionMetadataGeneratorDriver();
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
        TypeResolver = new TypeResolverImpl(this);
        
        CecilifiedLineNumber = ApiDriver.PreambleLineCount;
        StartLineNumber = ApiDriver.PreambleLineCount;
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
    }
    
    public new static IVisitorContext CreateContext(CecilifierOptions options, SemanticModel semanticModel) => new SystemReflectionMetadataContext(options, semanticModel);
}
