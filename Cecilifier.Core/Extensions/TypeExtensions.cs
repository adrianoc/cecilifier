using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.Extensions
{
    internal static class TypeExtensions
    {
        public static string ReflectionTypeName(this ITypeSymbol type, out IList<string> typeParameters)
        {
            if (type is INamedTypeSymbol namedType && namedType.IsGenericType) //TODO: namedType.IsUnboundGenericType ? Open 
            {
                typeParameters = namedType.TypeArguments.Select(typeArg => typeArg.FullyQualifiedName()).ToArray();
                return Regex.Replace(namedType.ConstructedFrom.ToString(), "<.*>", "`" + namedType.TypeArguments.Length );
            }

            typeParameters = Array.Empty<string>();
            return type.FullyQualifiedName();
        }

        public static string MakeByReferenceType(this string type)
        {
            return $"{type}.MakeByReferenceType()";
        }
    }

    public sealed class VariableDefinitionComparer : IEqualityComparer<VariableDefinition>
    {
        private static readonly Lazy<IEqualityComparer<VariableDefinition>> instance = new Lazy<IEqualityComparer<VariableDefinition>>(delegate { return new VariableDefinitionComparer(); });

        public static IEqualityComparer<VariableDefinition> Instance => instance.Value;

        public bool Equals(VariableDefinition x, VariableDefinition y)
        {
            if (x == null && y == null)
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            return x.Index == y.Index && x.VariableType.FullName == y.VariableType.FullName;
        }

        public int GetHashCode(VariableDefinition obj)
        {
            return obj.Index.GetHashCode() + 37 * obj.VariableType.FullName.GetHashCode();
        }
    }
}
