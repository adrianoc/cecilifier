using Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;
using Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;
using Cecilifier.Core;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

public class SystemReflectionMetadataContext : CecilifierContextBase, IVisitorContext
{
    private SystemReflectionMetadataContext(CecilifierOptions options, SemanticModel semanticModel, byte indentation = 2) : base(options, semanticModel, indentation)
    {
        AssemblyResolver = new SystemReflectionMetadataAssemblyResolver(this);
        ApiDriver = new SystemReflectionMetadataGeneratorDriver();
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
        TypeResolver = new SystemReflectionMetadataTypeResolver(this);
        MemberResolver = new SystemReflectionMetadataMemberResolver(this);
        
        CecilifiedLineNumber = ApiDriver.PreambleLineCount;
        StartLineNumber = ApiDriver.PreambleLineCount;
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
    }
    
    public static IVisitorContext CreateContext(CecilifierOptions options, SemanticModel semanticModel) => new SystemReflectionMetadataContext(options, semanticModel);
    
    public DelayedDefinitionsManager DelayedDefinitionsManager { get; } = new();
    
    public SystemReflectionMetadataAssemblyResolver AssemblyResolver { get; init; }
    
    public override void OnFinishedTypeDeclaration()
    {
        DelayedDefinitionsManager.ProcessDefinitions(this);
    }
    
    public SystemReflectionMetadataTypeResolver TypedTypeResolver => (SystemReflectionMetadataTypeResolver)TypeResolver;
}
