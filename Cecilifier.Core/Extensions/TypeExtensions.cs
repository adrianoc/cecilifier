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

        public static bool IsPrimitiveType(this ITypeSymbol type) => type.SpecialType switch {
            SpecialType.System_Boolean => true,
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Char => true,
            SpecialType.System_Double => true,
            SpecialType.System_Single => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            _ => false
        };
        
        public static uint SizeofArrayLikeItemElement(this ITypeSymbol type)
        {
            switch(type)
            {
                case INamedTypeSymbol { IsGenericType: true } ns : return SizeofArrayLikeItemElement(ns.TypeArguments[0]);
                case IPointerTypeSymbol ptr: return SizeofArrayLikeItemElement(ptr.PointedAtType);
                case IArrayTypeSymbol array: return SizeofArrayLikeItemElement(array.ElementType);
            }
            
            return type.SpecialType switch
            {
                SpecialType.System_Boolean => sizeof(bool),
                SpecialType.System_Byte => sizeof(byte),
                SpecialType.System_SByte => sizeof(sbyte),
                SpecialType.System_Char => sizeof(char),
                SpecialType.System_Double => sizeof(double),
                SpecialType.System_Single => sizeof(float),
                SpecialType.System_Int16 => sizeof(short),
                SpecialType.System_UInt16 => sizeof(ushort),
                SpecialType.System_Int32 => sizeof(int),
                SpecialType.System_UInt32 => sizeof(uint),
                SpecialType.System_Int64 => sizeof(long),
                SpecialType.System_UInt64 => sizeof(ulong),
                _ => throw new NotImplementedException()
            };
        }

        public static OpCode Stind(this ITypeSymbol type)
        {
            switch(type)
            {
                case INamedTypeSymbol { IsGenericType: true } ns : return Stind(ns.TypeArguments[0]);
                case IPointerTypeSymbol ptr: return Stind(ptr.PointedAtType);
                case IArrayTypeSymbol array: return Stind(array.ElementType);
            }
            
            return type.SpecialType switch
            {
                SpecialType.System_Boolean => OpCodes.Stind_I1,
                SpecialType.System_Byte => OpCodes.Stind_I1,
                SpecialType.System_SByte => OpCodes.Stind_I1,
                SpecialType.System_Char => OpCodes.Stind_I2,
                SpecialType.System_Double => OpCodes.Stind_R8,
                SpecialType.System_Single => OpCodes.Stind_R4,
                SpecialType.System_Int16 => OpCodes.Stind_I2,
                SpecialType.System_UInt16 => OpCodes.Stind_I2,
                SpecialType.System_Int32 => OpCodes.Stind_I4,
                SpecialType.System_UInt32 => OpCodes.Stind_I4,
                SpecialType.System_Int64 => OpCodes.Stind_I8,
                SpecialType.System_UInt64 => OpCodes.Stind_I8,
                _ => type.IsReferenceType 
                    ? OpCodes.Stind_Ref 
                    : throw new NotImplementedException($"Type = {type.Name}")
            };
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
