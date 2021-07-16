using System.Collections.Immutable;
using System.Linq;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            return str.Length > 1
                ? char.ToUpper(str[0]) + str.Substring(1)
                : str;
        }

        public static string MapModifier(this SyntaxToken modifier, string targetEnum)
        {
            switch (modifier.Kind())
            {
                case SyntaxKind.ProtectedKeyword: return targetEnum + ".Family";
                case SyntaxKind.InternalKeyword: return targetEnum + "." + (modifier.Parent.Kind() == SyntaxKind.ClassDeclaration ? "NotPublic" : "Assembly");
                case SyntaxKind.StaticKeyword: return targetEnum + "." + "Static";
            }

            return targetEnum + "." + modifier.ValueText.PascalCase();
        }

        public static string AppendModifier(this string to, string modifier)
        {
            if (string.IsNullOrWhiteSpace(modifier))
            {
                return to;
            }

            if (string.IsNullOrEmpty(to))
            {
                return modifier;
            }

            return to + " | " + modifier;
        }

        public static string AsCecilApplication(this string cecilSnippet, string assemblyName, string entryPointVar = null)
        {
            var moduleKind = entryPointVar == null ? "ModuleKind.Dll" : "ModuleKind.Console";
            var entryPointStatement = entryPointVar != null ? $"\t\t\tassembly.EntryPoint = {entryPointVar};\n" : string.Empty;
                 
            return $@"using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System; 
using System.Linq;
using BindingFlags = System.Reflection.BindingFlags;

using Cecilifier.Runtime;
               
public class SnippetRunner
{{
	public static void Main(string[] args)
	{{
		using(var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""{assemblyName}"", Version.Parse(""1.0.0.0"")), ""moduleName"", {moduleKind}))
        {{
{cecilSnippet}{entryPointStatement}
            PrivateCoreLibFixer.FixReferences(assembly.MainModule);
		    assembly.Write(args[0]);
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

            var found = toBeChecked.GetMembers().OfType<IMethodSymbol>().Where(candidate => CompareMethods(candidate, method)).SingleOrDefault();
            if (found == method || found == null)
            {
                found = FindLastDefinition(method, toBeChecked.Interfaces);
                found = found ?? FindLastDefinition(method, toBeChecked.BaseType);
            }

            return found;
        }

        private static IMethodSymbol FindLastDefinition(IMethodSymbol method, ImmutableArray<INamedTypeSymbol> implementedItfs)
        {
            foreach (var itf in implementedItfs)
            {
                var found = FindLastDefinition(method, itf);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool CompareMethods(IMethodSymbol lhs, IMethodSymbol rhs)
        {
            if (lhs.Name != rhs.Name)
            {
                return false;
            }

            if (lhs.ReturnType != rhs.ReturnType)
            {
                return false;
            }

            if (lhs.Parameters.Count() != rhs.Parameters.Count())
            {
                return false;
            }

            for (var i = 0; i < lhs.Parameters.Count(); i++)
            {
                if (lhs.Parameters[i].Type != rhs.Parameters[i].Type)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
