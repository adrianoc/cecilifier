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
        private static readonly IdGenerator idGenerator = new IdGenerator();

        public static string CamelCase(this string str)
        {
            return str.Length > 1
                ? char.ToUpper(str[0]) + str.Substring(1)
                : str;
        }

        public static int UniqueId(this string key)
        {
            return idGenerator.IdFor(key);
        }

        public static string MapModifier(this SyntaxToken modifier, string targetEnum)
        {
            switch (modifier.Kind())
            {
                case SyntaxKind.ProtectedKeyword: return targetEnum + ".Family";
                case SyntaxKind.InternalKeyword: return targetEnum + "." + (modifier.Parent.Kind() == SyntaxKind.ClassDeclaration ? "NotPublic" : "Assembly");
                case SyntaxKind.StaticKeyword: return targetEnum + "." + "Static";
            }

            return targetEnum + "." + modifier.ValueText.CamelCase();
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

        public static string AsCecilApplication(this string cecilSnippet)
        {
            return string.Format(
                @"using Mono.Cecil;
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
		var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""name"", Version.Parse(""1.0.0.0"")), ""moduleName"", ModuleKind.Dll);
{0}
		assembly.Write(args[0]);                              
	}}
}}", cecilSnippet);
        }

        public static IMethodSymbol FindLastDefinition(this IMethodSymbol self)
        {
            if (self == null)
            {
                return null;
            }

            return FindLastDefinition(self, self.ContainingType) ?? self;
        }

        public static BaseTypeDeclarationSyntax ResolveDeclaringType(this SyntaxNode node)
        {
            return new TypeDeclarationResolver().Resolve(node);
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
