using Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;
using Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;
using Cecilifier.Core;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

public class SystemReflectionMetadataContext : CecilifierContextBase, IVisitorContext
{
    private SystemReflectionMetadataContext(CecilifierOptions options, SemanticModel semanticModel, byte indentation = 2) : base(options, semanticModel, indentation)
    {
        AssemblyResolver = new SystemReflectionMetadataAssemblyResolver();
        ApiDriver = new SystemReflectionMetadataGeneratorDriver();
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
        TypeResolver = new SystemReflectionMetadataTypeResolver(this);
        MemberResolver = new SystemReflectionMetadataMemberResolver(this);
        
        CecilifiedLineNumber = ApiDriver.PreambleLineCount;
        StartLineNumber = ApiDriver.PreambleLineCount;
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
    }
    
    public DelayedDefinitionsManager DelayedDefinitionsManager { get; } = new();
    
    public SystemReflectionMetadataAssemblyResolver AssemblyResolver { get; init; }

    public override DefinitionVariable GetMethodVariable(IMethodSymbol method) => DefinitionVariables.GetMethodVariable(method.AsMethodVariable(VariableMemberKind.MethodSignature));

    public override void OnFinishedTypeDeclaration(INamedTypeSymbol typeSymbol)
    {
        // Only process delayed definitions if we're not processing a nested type.
        if(typeSymbol == null || typeSymbol.ContainingType == null)
            DelayedDefinitionsManager.ProcessDefinitions(this);
    }
    
    public SystemReflectionMetadataTypeResolver TypedTypeResolver => (SystemReflectionMetadataTypeResolver)TypeResolver;
    
    public static IVisitorContext CreateContext(CecilifierOptions options, SemanticModel semanticModel) => new SystemReflectionMetadataContext(options, semanticModel);

    public static string[] BclAssembliesForCompilation()
    {
        var aspnetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (aspnetEnvironment == "Production")
        {
            // since Cecilifier is deployed as a self-contained app, BCL assemblies are deployed to the same folder as the app.
            return Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrEmpty(dotnetRoot))
        {
            throw new Exception("DOTNET_ROOT environment variable is not set");
        }       
        return Directory.GetFiles($"{dotnetRoot}/packs/Microsoft.NETCore.App.Ref/{Environment.Version}/ref/net{Environment.Version.Major}.{Environment.Version.Minor}", "*.dll");
        
    }
}
