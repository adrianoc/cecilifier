using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataAssemblyResolver(SystemReflectionMetadataContext context)
{
    public string Resolve(IAssemblySymbol assembly)
    {
        return $"""
                metadata.AddAssemblyReference(
                                 name: metadata.GetOrAddString("{assembly.Name}"),
                                 version: new Version({assembly.Identity.Version.Major},{assembly.Identity.Version.Minor},{assembly.Identity.Version.MajorRevision},{assembly.Identity.Version.MinorRevision}),
                                 culture: default(StringHandle),
                                 publicKeyOrToken: metadata.GetOrAddBlob({GetPublicKey(assembly.Identity)}),
                                 flags: default(AssemblyFlags),
                                 hashValue: default(BlobHandle))

                """;
    }

    private static string GetPublicKey(AssemblyIdentity assemblyIdentity)
    {
        var values = assemblyIdentity.HasPublicKey ?  string.Join(',', assemblyIdentity.PublicKeyToken.ToArray()) : "";
        return $"ImmutableArray.Create<byte>({values})";
    }
}
