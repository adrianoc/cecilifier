using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            return str.Length > 1
                ? char.ToLower(str[0]) + str.Substring(1)
                : str;
        }
        
        public static string PascalCase(this string str)
        {
            Span<char> copySpan = stackalloc char[str.Length];
            str.AsSpan().CopyTo(copySpan);
            
            if (copySpan.Length > 1) 
                copySpan[0] = Char.ToUpper(copySpan[0]);

            return copySpan.ToString();
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

            const string runtimeConfigJsonExt = ".runtimeconfig.json";
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
        // setup a `reflection importer` to ensure references to System.Private.CoreLib are replaced with references to `netstandard`. 
        var mp = new ModuleParameters {{ Architecture = TargetArchitecture.AMD64, Kind =  {moduleKind}, ReflectionImporterProvider = new SystemPrivateCoreLibFixerReflectionProvider() }};
		using(var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""{assemblyName}"", Version.Parse(""1.0.0.0"")), Path.GetFileName(args[0]), mp))
        {{
{cecilSnippet}{entryPointStatement}
		    assembly.Write(args[0]);

            //Writes a {runtimeConfigJsonExt} file matching the output assembly name.
			File.Copy(
				Path.ChangeExtension(typeof(SnippetRunner).Assembly.Location, ""{runtimeConfigJsonExt}""),
                Path.ChangeExtension(args[0], ""{runtimeConfigJsonExt}""),
                true);
        }}
	}}
}}";
        }

        public static IMethodSymbol FindLastDefinition(this IMethodSymbol self)
        {
            if (self == null)
            {
                return null;
            }

            return FindLastDefinition(self, self.ContainingType) ?? self;
        }

        public static T ResolveDeclaringType<T>(this SyntaxNode node) where T : BaseTypeDeclarationSyntax
        {
            return (T) new TypeDeclarationResolver().Resolve(node);
        }

        private static IMethodSymbol FindLastDefinition(IMethodSymbol method, INamedTypeSymbol toBeChecked)
        {
            if (toBeChecked == null)
            {
                return null;
            }

            var found = toBeChecked.GetMembers().OfType<IMethodSymbol>().SingleOrDefault(candidate => CompareMethods(candidate, method));
            if (SymbolEqualityComparer.Default.Equals(found, method) || found == null)
            {
                found = FindLastDefinition(method, toBeChecked.Interfaces);
                found = found ?? FindLastDefinition(method, toBeChecked.BaseType);
            }

            return found;
        }

        public static IMethodSymbol FindLastDefinition(this IMethodSymbol method, ImmutableArray<INamedTypeSymbol> implementedItfs)
        {
            foreach (var itf in implementedItfs)
            {
                var found = FindLastDefinition(method, itf);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static bool CompareMethods(IMethodSymbol lhs, IMethodSymbol rhs)
        {
            if (lhs.Name != rhs.Name)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(lhs.ReturnType, rhs.ReturnType))
                return false;

            if (lhs.Parameters.Count() != rhs.Parameters.Count())
                return false;

            for (var i = 0; i < lhs.Parameters.Count(); i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(lhs.Parameters[i].Type, rhs.Parameters[i].Type))
                    return false;
            }

            return true;
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
                sb.AppendLine($"{mapping.Node.HumanReadableSummary() ,60} {mapping.Source} <- -> {mapping.Cecilified}");
            }
#endif
            return sb.ToString();
        }
    }
}
