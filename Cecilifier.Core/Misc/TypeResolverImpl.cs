using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc
{
    internal class TypeResolverImpl : ITypeResolver
    {
        private readonly CecilifierContext _context;

        public TypeResolverImpl(CecilifierContext context)
        {
            _context = context;
            Bcl = new Bcl(this, _context);
        }

        public Bcl Bcl { get; }

        public string Resolve(ITypeSymbol type, string cecilTypeParameterProviderVar = null)
        {
            return ResolveLocalVariableType(type)
                   ?? ResolvePredefinedAndComposedTypes(type)
                   ?? ResolveGenericType(type, cecilTypeParameterProviderVar)
                   ?? ResolveTypeParameter(type, cecilTypeParameterProviderVar)
                   ?? Resolve(type.ToDisplayString());
        }

        public string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");

        public string ResolvePredefinedType(ITypeSymbol type) => $"assembly.MainModule.TypeSystem.{type.Name}";

        private string ResolvePredefinedAndComposedTypes(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return Resolve(array.ElementType) + ".MakeArrayType()";
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return Resolve(pointerType.PointedAtType) + ".MakePointerType()";
            }

            if (type is IFunctionPointerTypeSymbol functionPointer)
            {
                return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
            }
            
            if (type.SpecialType == SpecialType.None 
                || type.SpecialType == SpecialType.System_Array 
                || type.SpecialType == SpecialType.System_Enum 
                || type.SpecialType == SpecialType.System_ValueType 
                || type.SpecialType == SpecialType.System_Decimal 
                || type.SpecialType == SpecialType.System_DateTime
                || type.SpecialType == SpecialType.System_Delegate
                || type.TypeKind == TypeKind.Interface)
            {
                return null;
            }

            return ResolvePredefinedType(type);
        }
        
        private string ResolveTypeParameter(ITypeSymbol type, string cecilTypeParameterProviderVar)
        {
            if (type is not ITypeParameterSymbol typeParameterSymbol)
                return null;

            if (cecilTypeParameterProviderVar == null)
                return null;
            
            return typeParameterSymbol.ContainingSymbol.Kind switch
            {
                SymbolKind.NamedType => $"(({cecilTypeParameterProviderVar} is MethodReference methodReference) ? ((GenericInstanceType) methodReference.DeclaringType).ElementType : (IGenericParameterProvider) {cecilTypeParameterProviderVar} ).GenericParameters[{typeParameterSymbol.Ordinal}]",
                SymbolKind.Method => $"{cecilTypeParameterProviderVar}.GenericParameters[{typeParameterSymbol.Ordinal}]",
                _ => null
            };
        }
        
        private string ResolveGenericType(ITypeSymbol type, string cecilTypeParameterProviderVar)
        {
            if (!(type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } genericTypeSymbol))
            {
                return null;
            }

            if (type.IsTupleType)
            {
                return null;
            }

            var genericType = Resolve(OpenGenericTypeName(genericTypeSymbol.ConstructedFrom));
            return genericTypeSymbol.IsDefinition
                ? genericType
                : MakeGenericInstanceType(genericType, genericTypeSymbol, cecilTypeParameterProviderVar);
        }

        public string ResolveLocalVariableType(ITypeSymbol type)
        {
            var containingSymbolName = type.ContainingSymbol?.FullyQualifiedName(false);
            var found = _context.DefinitionVariables.GetVariable(type.Name, VariableMemberKind.Type, containingSymbolName).VariableName
                        ?? _context.DefinitionVariables.GetVariable(type.Name, VariableMemberKind.TypeParameter, containingSymbolName).VariableName;

            if (found != null && type is INamedTypeSymbol { IsGenericType: true } genericTypeSymbol)
            {
                return MakeGenericInstanceType(found, genericTypeSymbol, null);
            }

            return found;
        }

        private IList<string> CollectTypeArguments(INamedTypeSymbol typeArgumentProvider, List<string> collectTo, string cecilTypeParameterProviderVar)
        {
            if (typeArgumentProvider.ContainingType != null)
            {
                CollectTypeArguments(typeArgumentProvider.ContainingType, collectTo, cecilTypeParameterProviderVar);
            }
            collectTo.AddRange(typeArgumentProvider.TypeArguments.Where(t => t.Kind != SymbolKind.ErrorType).Select(t => Resolve(t, cecilTypeParameterProviderVar)));

            return collectTo;
        }

        private string MakeGenericInstanceType(string typeReference, INamedTypeSymbol genericTypeSymbol, string cecilTypeParameterProviderVar)
        {
            var typeArgs = CollectTypeArguments(genericTypeSymbol, new List<string>(), cecilTypeParameterProviderVar);
            return typeArgs.Count > 0
                ? typeReference.MakeGenericInstanceType(typeArgs)
                : typeReference;
        }

        private string OpenGenericTypeName(ITypeSymbol type)
        {
            var genericTypeWithTypeParameters = type.ToString();

            var genOpenBraceIndex = genericTypeWithTypeParameters.IndexOf('<');
            var genCloseBraceIndex = genericTypeWithTypeParameters.LastIndexOf('>');

            var nts = (INamedTypeSymbol) type;
            var commas = new string(',', nts.TypeParameters.Length - 1);
            return genericTypeWithTypeParameters.Remove(genOpenBraceIndex + 1, genCloseBraceIndex - genOpenBraceIndex - 1).Insert(genOpenBraceIndex + 1, commas);
        }
    }
}
