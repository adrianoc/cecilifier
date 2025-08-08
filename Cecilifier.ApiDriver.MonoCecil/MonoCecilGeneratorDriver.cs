using Cecilifier.Core;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilGeneratorDriver : IILGeneratorApiDriver
{
    public string AsCecilApplication(string cecilifiedCode, string mainTypeName, string? entryPointVar)
    {
        var moduleKind = entryPointVar == null ? "ModuleKind.Dll" : "ModuleKind.Console";
        var entryPointStatement = entryPointVar != null ? $"\t\t\tassembly.EntryPoint = {entryPointVar};\n" : string.Empty;

        return $@"using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System; 
using System.IO;
using System.Linq;
using BindingFlags = System.Reflection.BindingFlags;
using Cecilifier.Runtime;
               
public class SnippetRunner
{{
	public static void Main(string[] args)
	{{
        // setup `reflection/metadata importers` to ensure references to System.Private.CoreLib are replaced with references to the correct reference assemblies`.
        var mp = new ModuleParameters
        {{
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==  System.Runtime.InteropServices.Architecture.Arm64 ? TargetArchitecture.ARM64 : TargetArchitecture.AMD64,
            Kind =  {moduleKind},
            MetadataImporterProvider = new SystemPrivateCoreLibFixerMetadataImporterProvider(),
            ReflectionImporterProvider = new SystemPrivateCoreLibFixerReflectionProvider()
        }};

		using(var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""{mainTypeName}"", Version.Parse(""1.0.0.0"")), Path.GetFileName(args[0]), mp))
        {{
{cecilifiedCode}{entryPointStatement}
		    assembly.Write(args[0]);

            //Writes a {Constants.Common.RuntimeConfigJsonExt} file matching the output assembly name.
			File.Copy(
				Path.ChangeExtension(typeof(SnippetRunner).Assembly.Location, ""{Constants.Common.RuntimeConfigJsonExt}""),
                Path.ChangeExtension(args[0], ""{Constants.Common.RuntimeConfigJsonExt}""),
                true);
        }}
	}}
}}";
    }

    public int PreambleLineCount { get; init; } = 25; // The # of lines before the 1st cecilified line of code (see `cecilifiedCode` parameter from AsCecilApplication())
}
