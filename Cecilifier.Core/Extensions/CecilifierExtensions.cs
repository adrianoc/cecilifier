using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions
{
    public static class CecilifierExtensions
    {
        public static string CamelCase(this string str)
        {
            if (str.Length < 2)
                return str;
            
            return string.Create(str.Length, str, (span, value) =>
            {
                str.AsSpan().CopyTo(span);
                span[0] = char.ToLowerInvariant(span[0]);
            });
        }

        public static string PascalCase(this string str)
        {
            if (str.Length < 2)
                return str;
            
            return string.Create(str.Length, str, (span, value) =>
            {
                str.AsSpan().CopyTo(span);
                span[0] = char.ToUpperInvariant(span[0]);
            });
        }

        public static string AppendModifier(this string to, string modifier)
        {
            if (string.IsNullOrWhiteSpace(modifier))
                return to;

            if (string.IsNullOrEmpty(to))
                return modifier;

            return $"{to} | {modifier}";
        }

        public static StringBuilder AppendModifier(this StringBuilder to, string modifier)
        {
            if (string.IsNullOrWhiteSpace(modifier))
            {
                return to;
            }

            if (to.Length != 0)
            {
                to.Append(" | ");
            }

            to.Append(modifier);
            return to;
        }

        public static string AsCecilApplication(this string cecilSnippet, string assemblyName, string entryPointVar = null)
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

		using(var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""{assemblyName}"", Version.Parse(""1.0.0.0"")), Path.GetFileName(args[0]), mp))
        {{
{cecilSnippet}{entryPointStatement}
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

        public static T ResolveDeclaringType<T>(this SyntaxNode node) where T : BaseTypeDeclarationSyntax
        {
            return (T) new TypeDeclarationResolver().Resolve(node);
        }

        [Conditional("DEBUG")]
        public static void DumpTo(this IList<Mapping> self, TextWriter textWriter)
        {
            textWriter.WriteLine(self.DumpAsString());
        }

        public static string DumpAsString(this IList<Mapping> self)
        {
            var sb = new StringBuilder();
#if DEBUG            
            foreach (var mapping in self)
            {
                sb.AppendLine($"{mapping.Node.HumanReadableSummary(),60} {mapping.Source} <- -> {mapping.Cecilified}");
            }
#endif
            return sb.ToString();
        }
    }
}
