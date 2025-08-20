using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc
{
    public class TypeResolverImpl : ITypeResolver
    {
        private readonly IVisitorContext _context;

        public TypeResolverImpl(IVisitorContext context)
        {
            _context = context;
            Bcl = new Bcl(this, _context);
        }

        public Bcl Bcl { get; }

        public string Resolve(ITypeSymbol type, string cecilTypeParameterProviderVar = null)
        {
            return ResolveLocalVariableType(type)
                   ?? ResolveNestedType(type)
                   ?? ResolvePredefinedAndComposedTypes(type)
                   ?? ResolveGenericType(type, cecilTypeParameterProviderVar)
                   ?? ResolveTypeParameter(type, cecilTypeParameterProviderVar)
                   ?? Resolve(type.ToDisplayString());
        }

        public string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");
        
        private string ResolveNestedType(ITypeSymbol type)
        {
            if (type.ContainingType == null || type.Kind == SymbolKind.TypeParameter)
                return null;

            if (type is INamedTypeSymbol { IsGenericType: true } nestedType 
                && (nestedType.HasTypeArgumentOfTypeFromCecilifiedCodeTransitive(_context) || nestedType.ContainingType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter()))
            {
                // collects the type arguments for all types in the parent chain. 
                var typeArguments = nestedType.GetAllTypeArguments().ToArray();
                var resolveNestedType = $"""TypeHelpers.NewRawNestedTypeReference("{type.Name}", module: assembly.MainModule, {Resolve(type.ContainingType.OriginalDefinition)}, isValueType: {(type.IsValueType ? "true" : "false")}, {typeArguments.Length})""";
            
                // if type is a generic type definition we return the open, resolved type
                // otherwise this method is expected to return a 'GenericInstanceType'.
                // Note that in this case even if the parent type is the generic one,
                // in IL, we need to create a 'GenericInstanceType' of the nested 
                // (irrespective to it being a generic type or not). For instance, 
                // to represent the type 'List<string>.Enumerator', a 'TypeReference'
                // for 'Enumerator' is instantiated and a 'generic parameter' is added
                // to it (even though it is *not* a generic type, it's parent type is
                // and the parent's type generic parameters are added to the nested type) 
                return type.IsDefinition 
                    ? resolveNestedType 
                    : resolveNestedType.MakeGenericInstanceType(typeArguments.Select(t => _context.TypeResolver.Resolve(t)).ToArray());
            }

            return null;
        }

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
            var containingSymbolName = type.ContainingSymbol?.OriginalDefinition.ToDisplayString();
            var found = _context.DefinitionVariables.GetVariable(type.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, containingSymbolName).VariableName
                        ?? _context.DefinitionVariables.GetVariable(type.OriginalDefinition.ToDisplayString(), VariableMemberKind.TypeParameter, containingSymbolName).VariableName;

            if (found != null && type is INamedTypeSymbol { IsGenericType: true } genericTypeSymbol)
            {
                return MakeGenericInstanceType(found, genericTypeSymbol, found);
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
