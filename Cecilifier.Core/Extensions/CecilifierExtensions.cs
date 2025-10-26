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
