using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataAssemblyResolver()
{
    public string Resolve(SystemReflectionMetadataContext context, IAssemblySymbol assembly)
    {
        var existingVar = context.DefinitionVariables.GetVariable(assembly.ToDisplayString(), VariableMemberKind.None);
        if (existingVar.IsValid)
            return existingVar.VariableName;

        var assemblyReferenceName = context.Naming
                                            .Without(NamingOptions.SuffixVariableNamesWithUniqueId | NamingOptions.CamelCaseElementNames | NamingOptions.SeparateCompoundWords)
                                            .SyntheticVariable($"{assembly.Name}", ElementKind.AssemblyReference);
        
        context.DefinitionVariables.RegisterNonMethod(string.Empty, assembly.ToDisplayString(),  VariableMemberKind.None, assemblyReferenceName);
        context.Generate($"""
                                 var {assemblyReferenceName} = metadata.AddAssemblyReference(
                                                                      name: metadata.GetOrAddString("{assembly.Name}"),
                                                                      version: new Version({assembly.Identity.Version.Major},{assembly.Identity.Version.Minor},{assembly.Identity.Version.MajorRevision},{assembly.Identity.Version.MinorRevision}),
                                                                      culture: default(StringHandle),
                                                                      publicKeyOrToken: metadata.GetOrAddBlob({GetPublicKey(assembly.Identity)}),
                                                                      flags: default(AssemblyFlags),
                                                                      hashValue: default(BlobHandle));
                                 """);
        context.WriteNewLine();
        
        return assemblyReferenceName;
    }

    private static string GetPublicKey(AssemblyIdentity assemblyIdentity)
    {
        var values = assemblyIdentity.HasPublicKey ?  string.Join(',', assemblyIdentity.PublicKeyToken.ToArray()) : "";
        return $"ImmutableArray.Create<byte>({values})";
    }
}
