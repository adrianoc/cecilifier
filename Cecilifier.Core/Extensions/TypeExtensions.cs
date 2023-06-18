using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
    internal static class TypeExtensions
    {
        public static bool IsNonPrimitiveValueType(this ITypeSymbol type, IVisitorContext context) => !type.IsPrimitiveType() 
                                                                                                      && (type.IsValueType || SymbolEqualityComparer.Default.Equals(type, context.RoslynTypeSystem.SystemValueType));
        
        public static string MakeByReferenceType(this string type)
        {
            return $"{type}.MakeByReferenceType()";
        }
        public static string MakeGenericInstanceType(this string type, IEnumerable<string> genericTypes)
        {
            return $"{type}.MakeGenericInstanceType({string.Join(", ", genericTypes)})";
        }

        public static string MakeGenericInstanceType(this string type, string genericType)
        {
            return $"{type}.MakeGenericInstanceType({genericType})";
        }

        public static bool IsPrimitiveType(this ITypeSymbol type) => type.SpecialType switch
        {
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

        public static ITypeSymbol ElementTypeSymbolOf(this ITypeSymbol type) => type switch
        {
            INamedTypeSymbol { IsGenericType: true, OriginalDefinition: { ContainingNamespace.Name: "System", Name: "Span" } } ns =>  ns.TypeArguments[0],
            IPointerTypeSymbol ptr => ptr.PointedAtType,
            IArrayTypeSymbol array =>  array.ElementType,
            
            _ => throw new ArgumentException($"{type.Name} not supported.", nameof(type))
        };

        public static uint SizeofArrayLikeItemElement(this ITypeSymbol type)
        {
            switch (type)
            {
                case INamedTypeSymbol { IsGenericType: true } ns:
                    return SizeofArrayLikeItemElement(ns.TypeArguments[0]);
                case IPointerTypeSymbol ptr:
                    return SizeofArrayLikeItemElement(ptr.PointedAtType);
                case IArrayTypeSymbol array:
                    return SizeofArrayLikeItemElement(array.ElementType);
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
            switch (type)
            {
                case INamedTypeSymbol { IsGenericType: true } ns:
                    return Stind(ns.TypeArguments[0]);
                case IPointerTypeSymbol ptr:
                    return Stind(ptr.PointedAtType);
                case IArrayTypeSymbol array:
                    return Stind(array.ElementType);
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
                    : OpCodes.Stobj
            };
        }

        public static OpCode StelemOpCode(this ITypeSymbol type) =>
            type.SpecialType switch
            {
                SpecialType.System_Byte => OpCodes.Stelem_I1,
                SpecialType.System_Char => OpCodes.Stelem_I2,
                SpecialType.System_Int16 => OpCodes.Stelem_I2,
                SpecialType.System_Int32 => OpCodes.Stelem_I4,
                SpecialType.System_Int64 => OpCodes.Stelem_I8,
                SpecialType.System_Single => OpCodes.Stelem_R4,
                SpecialType.System_Double => OpCodes.Stelem_R8,
                SpecialType.None => type.IsValueType ? OpCodes.Stelem_Any : OpCodes.Stelem_Ref, // Any => Custom structs, Ref => class.
                SpecialType.System_String => OpCodes.Stelem_Ref,
                SpecialType.System_Object => OpCodes.Stelem_Ref,
                _ => type.IsValueType ? OpCodes.Stelem_Any : throw new Exception($"Element type {type.Name} not supported.")
            };
        

        public static bool IsTypeParameterOrIsGenericTypeReferencingTypeParameter(this ITypeSymbol returnType) => 
            returnType.TypeKind == TypeKind.TypeParameter
            || returnType is INamedTypeSymbol { IsGenericType: true } genType && genType.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter);
    }

    public sealed class VariableDefinitionComparer : IEqualityComparer<VariableDefinition>
    {
        private static readonly Lazy<IEqualityComparer<VariableDefinition>> instance = new(() => new VariableDefinitionComparer());

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
