using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;
using Cecilifier.Core.AST;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace Cecilifier.Core.Extensions
{
    public static class TypeExtensions
    {
        public static bool IsNonPrimitiveValueType(this ITypeSymbol type, IVisitorContext context) => !type.IsPrimitiveType() 
                                                                                                      && (type.IsValueType || SymbolEqualityComparer.Default.Equals(type, context.RoslynTypeSystem.SystemValueType));
        
        public static string MakeByReferenceType(this string type)
        {
            return $"{type}.MakeByReferenceType()";
        }
        
        public static string MakeGenericInstanceType(this string type, IEnumerable<string> typeArguments)
        {
            return $"{type}.MakeGenericInstanceType({string.Join(", ", typeArguments)})";
        }

        public static string MakeGenericInstanceType(this string type, string typeArgument)
        {
            return $"{type}.MakeGenericInstanceType({typeArgument})";
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
            INamedTypeSymbol { IsGenericType: true, OriginalDefinition: { ContainingNamespace.Name: "System", Name: "ReadOnlySpan" } } ns =>  ns.TypeArguments[0],
            IPointerTypeSymbol ptr => ptr.PointedAtType,
            IArrayTypeSymbol array =>  array.ElementType,
            _ => type
        };

        public static int SizeofPrimitiveType(this ITypeSymbol type)
        {
            switch (type)
            {
                case INamedTypeSymbol { IsGenericType: true } ns:
                    return SizeofPrimitiveType(ns.TypeArguments[0]);
                case IPointerTypeSymbol ptr:
                    return SizeofPrimitiveType(ptr.PointedAtType);
                case IArrayTypeSymbol array:
                    return SizeofPrimitiveType(array.ElementType);
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
                _ => throw new NotImplementedException($"Type not supported: {type}")
            };
        }

        public static OpCode StindOpCodeFor(this ITypeSymbol type)
        {
            switch (type)
            {
                case INamedTypeSymbol { IsGenericType: true } ns:
                    return StindOpCodeFor(ns.TypeArguments[0]);
                case IPointerTypeSymbol ptr:
                    return StindOpCodeFor(ptr.PointedAtType);
                case IArrayTypeSymbol array:
                    return StindOpCodeFor(array.ElementType);
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

        public static OpCode LdindOpCodeFor(this ITypeSymbol type)
        {
            return type.SpecialType switch
            {
                SpecialType.System_Single => OpCodes.Ldind_R4,
                SpecialType.System_Double => OpCodes.Ldind_R8,
                SpecialType.System_SByte => OpCodes.Ldind_I1,
                SpecialType.System_Byte => OpCodes.Ldind_U1,
                SpecialType.System_Int16 => OpCodes.Ldind_I2,
                SpecialType.System_UInt16 => OpCodes.Ldind_U2,
                SpecialType.System_Int32 => OpCodes.Ldind_I4,
                SpecialType.System_UInt32 => OpCodes.Ldind_U4,
                SpecialType.System_Int64 => OpCodes.Ldind_I8,
                SpecialType.System_UInt64 => OpCodes.Ldind_I8,
                SpecialType.System_Char => OpCodes.Ldind_U2,
                SpecialType.System_Boolean => OpCodes.Ldind_U1,
                SpecialType.System_Object => OpCodes.Ldind_Ref,
                
                _ => type.IsValueType || type.IsTypeParameterOrIsGenericTypeReferencingTypeParameter() ? OpCodes.Ldobj : OpCodes.Ldind_Ref
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
                SpecialType.None => type.IsValueType ? OpCodes.Stelem : OpCodes.Stelem_Ref, // Any => Custom structs, Ref => class.
                SpecialType.System_String => OpCodes.Stelem_Ref,
                SpecialType.System_Object => OpCodes.Stelem_Ref,
                _ => type.IsValueType ? OpCodes.Stelem : throw new Exception($"Element type {type.Name} not supported.")
            };
        
        public static OpCode LdelemOpCode(this ITypeSymbol type) =>
            type.SpecialType switch
            {
                SpecialType.System_Byte => OpCodes.Ldelem_I1,
                SpecialType.System_Char => OpCodes.Ldelem_I2,
                SpecialType.System_Int16 => OpCodes.Ldelem_I2,
                SpecialType.System_Int32 => OpCodes.Ldelem_I4,
                SpecialType.System_Int64 => OpCodes.Ldelem_I8,
                SpecialType.System_Single => OpCodes.Ldelem_R4,
                SpecialType.System_Double => OpCodes.Ldelem_R8,
                SpecialType.None => (type.IsValueType || type.TypeKind == TypeKind.TypeParameter) ? OpCodes.Ldelem : OpCodes.Ldelem_Ref, // Any => Custom structs, Ref => class.
                SpecialType.System_String => OpCodes.Ldelem_Ref,
                SpecialType.System_Object => OpCodes.Ldelem_Ref,
                _ => type.IsValueType ? OpCodes.Ldelem : throw new Exception($"Element type {type.Name} not supported.")
            };

        public static bool IsTypeParameterOrIsGenericTypeReferencingTypeParameter(this ITypeSymbol type) => 
            type.TypeKind == TypeKind.TypeParameter
            || type is INamedTypeSymbol { IsGenericType: true } genType && genType.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter);
        
        public static bool IsTypeParameterConstrainedToReferenceType(this ITypeSymbol typeSymbol) => 
            typeSymbol is ITypeParameterSymbol typeParameterSymbol 
            && (typeParameterSymbol.HasReferenceTypeConstraint || typeParameterSymbol.ConstraintTypes.Length > 0);

        /// <summary>
        /// Returns a list of type arguments used in the generic type instantiation of <paramref name="type"/> 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <remarks>
        /// For nested types this list includes all type's type arguments and *all* type arguments from *all*
        /// parent types.
        ///
        /// For instance, given the type <![CDATA[Foo<int>.Bar<string,bool>.FooBar]]> this method will return
        /// [int, string, bool]
        /// </remarks>
        public static IEnumerable<ITypeSymbol> GetAllTypeArguments(this INamedTypeSymbol type)
        {
            if (type.ContainingType == null)
                return type.TypeArguments;
                
            return type.ContainingType.GetAllTypeArguments().Concat(type.TypeArguments);
        }

        public static CilOperandValue ToCilOperandValue(this ITypeSymbol type, object value) => new(type, value);
    }
}
