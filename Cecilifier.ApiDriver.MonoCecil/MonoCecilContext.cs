using Cecilifier.ApiDriver.MonoCecil.TypeSystem;
using Cecilifier.Core;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilContext : CecilifierContextBase, IVisitorContext
{
    public MonoCecilContext(CecilifierOptions options, SemanticModel semanticModel, byte indentation = 3) : base(options, semanticModel, indentation)
    {
        MemberResolver = new MonoCecilMemberResolver(this);
        TypeResolver = new MonoCecilTypeResolver(this);
        ApiDriver = new MonoCecilGeneratorDriver();
        
        CecilifiedLineNumber = ApiDriver.PreambleLineCount;
        StartLineNumber = ApiDriver.PreambleLineCount;
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
    }

    public static IVisitorContext CreateContext(CecilifierOptions options, SemanticModel semanticModel) => new MonoCecilContext(options, semanticModel);
    public static string[] BclAssembliesForCompilation()
    {
        return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
    }

    public override void OnFinishedTypeDeclaration(INamedTypeSymbol _) { } // Nothing to do here for Mono.Cecil

    public override DefinitionVariable GetMethodVariable(IMethodSymbol method) => DefinitionVariables.GetMethodVariable(method.AsMethodDefinitionVariable());
}
