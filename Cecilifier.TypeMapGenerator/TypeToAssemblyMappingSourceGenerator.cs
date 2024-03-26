using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Cecilifier.TypeMapGenerator;

#pragma warning disable RS1035

[Generator]
public class SampleIncrementalSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForPostInitialization(c =>
        {
            c.AddSource(
                "TypeToAssemblyNameReferenceMap.gen.cs",
                $$"""
                // Generated on {{DateTime.Now}}
                namespace Cecilifier.Runtime;
                using Mono.Cecil;

                internal partial class PrivateCorlibFixerMixin
                {
                    partial void InitializeTypeToAssemblyNameReferenceMap()
                    {
                        {{GenerateTypeToAssemblyNameReferenceInitializationCode()}}
                    }
                }
                """);
        });
    }
    
    public void Execute(GeneratorExecutionContext context)
    {
    }

    private string GenerateTypeToAssemblyNameReferenceInitializationCode()
    {
        StringBuilder initialization = new();
        
        var dotnetReferenceAssembliesFolder = Path.GetDirectoryName(typeof(object).Assembly.Location);

        dotnetReferenceAssembliesFolder = dotnetReferenceAssembliesFolder
                                              .Replace("shared", "packs")
                                              .Replace(".App", ".App.Ref")
                                          + $"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}net{Environment.Version.Major}.{Environment.Version.Minor}";
       
        // Maps from something like /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.2/
        // to :                    /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.2/ref/net8.0/
        // i.e, 
        // shared => packs
        // .App => .App.Ref
        // Append netX.Y

        Dictionary<int, string> assemblyNameReferenceCache = new();
        Dictionary<string, (string VarName, string TypeName)> typeToAssemblyReferenceVar = new();
        var files = Directory.GetFiles(dotnetReferenceAssembliesFolder, "*.dll").Where(path => !path.Contains("netstandard"));
        foreach(var assemblyPath in files)
        {
            initialization.Append(CollectAssemblyReferences(assemblyPath, assemblyNameReferenceCache, typeToAssemblyReferenceVar));
        }
        
        initialization.Append("""
                            _typeToAssemblyNameReference = new()
                            {
                            """);
        foreach (var item in typeToAssemblyReferenceVar)
        {
            initialization.Append($"""["{item.Key}"] = {item.Value.VarName},// {item.Value.TypeName}{Environment.NewLine}""");
        }

        initialization.Append("};");
        
        return initialization.ToString();
    }
    
    string CollectAssemblyReferences(string assemblyPath, Dictionary<int, string> assemblyNameReferenceCache, Dictionary<string, (string, string)> typeToAssemblyReferenceVar)
    {
        using var fs = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var peReader = new PEReader(fs);
        var metadataReader = peReader.GetMetadataReader();

        StringBuilder assemblyReferences = new();

        int index = assemblyNameReferenceCache.Count;
        var assemblyName = metadataReader.GetAssemblyDefinition().GetAssemblyName();
        string assemblyNameReference;
        
        assemblyReferences.Append($"""var ar{index} = AssemblyNameReference.Parse("{ assemblyName.FullName }");{Environment.NewLine}""");
        assemblyNameReferenceCache[assemblyName.FullName.GetHashCode()] = assemblyNameReference = $"ar{index}";

        foreach(var td in metadataReader.TypeDefinitions.Select(th => metadataReader.GetTypeDefinition(th)).Where(IsPublic))
        {
            var fullName = $"{metadataReader.GetString(td.Namespace)}.{metadataReader.GetString(td.Name)}";
            typeToAssemblyReferenceVar[fullName] = (assemblyNameReference, fullName);  
        }

        foreach(var et in metadataReader.ExportedTypes.Select(th => metadataReader.GetExportedType(th)).Where(candidate => (candidate.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.NotPublic))
        {
            if (et.Implementation.Kind != HandleKind.AssemblyReference)
            {
                Console.WriteLine($"Dont know how to resolve assembly reference for {et.Namespace}.{et.Name} ({et.Implementation.Kind})");
                continue;
            }
            
            assemblyName = metadataReader.GetAssemblyReference((AssemblyReferenceHandle) et.Implementation).GetAssemblyName();
            if (!assemblyNameReferenceCache.TryGetValue(assemblyName.FullName.GetHashCode(), out assemblyNameReference))
            {
                index++;
                assemblyReferences.Append($"""var ar{index} = AssemblyNameReference.Parse("{ assemblyName.FullName }");{Environment.NewLine}""");
                assemblyNameReferenceCache[assemblyName.FullName.GetHashCode()] = assemblyNameReference = $"ar{index}";
            }

            var fullName = $"{metadataReader.GetString(et.Namespace)}.{metadataReader.GetString(et.Name)}";
            typeToAssemblyReferenceVar[fullName] = (assemblyNameReference, fullName);
        }

        return assemblyReferences.ToString();

        static bool IsPublic(TypeDefinition typeDefinition) => (typeDefinition.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.NotPublic;
    }
}
