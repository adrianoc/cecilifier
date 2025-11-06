using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem
{
    public abstract class TypeResolverBase<TContext> : ITypeResolver where TContext : IVisitorContext
    {
        protected readonly TContext _context;

        protected TypeResolverBase(TContext context)
        {
            _context = context;
            Bcl = new Bcl(this, _context);
        }

        public Bcl Bcl { get; }

        // Resolvers for api drivers that need to generate different code based on the target can override this method.
        public virtual ResolvedType ResolveAny(ITypeSymbol type, ResolveTargetKind resolveTargetKind = ResolveTargetKind.None, string cecilTypeParameterProviderVar = null)
        {
            var resolvedType = ResolveLocalVariableType(type);
            if (resolvedType)
                return resolvedType;
            
            resolvedType = ResolveNestedType(type);
            if (resolvedType)
                return resolvedType;
            
            resolvedType = ResolvePredefinedAndComposedTypes(type, resolveTargetKind);
            if (resolvedType)
                return resolvedType;

            resolvedType = ResolveGenericType(type, cecilTypeParameterProviderVar);
            if (resolvedType)
                return resolvedType;
                    
            resolvedType = ResolveTypeParameter(type, cecilTypeParameterProviderVar);
            if (resolvedType)
                return resolvedType;
                
            return Resolve(type);
        }
       
        private ResolvedType ResolveNestedType(ITypeSymbol type)
        {
            if (type.ContainingType == null || type.Kind == SymbolKind.TypeParameter)
                return null;

            if (type is INamedTypeSymbol { IsGenericType: true } nestedType 
                && (nestedType.HasTypeArgumentOfTypeFromCecilifiedCodeTransitive(_context) || nestedType.ContainingType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter()))
            {
                // collects the type arguments for all types in the parent chain. 
                var typeArguments = nestedType.GetAllTypeArguments().ToArray();
                var resolveNestedType = new ResolvedType($"""TypeHelpers.NewRawNestedTypeReference("{type.Name}", module: assembly.MainModule, {ResolveAny(type.ContainingType.OriginalDefinition)}, isValueType: {type.IsValueType.ToKeyword()}, {typeArguments.Length})""");
            
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
                    : resolveNestedType.MakeGenericInstanceType(typeArguments.Select(t => _context.TypeResolver.ResolveAny(t)).ToArray());
            }

            return null;
        }

        public virtual ResolvedType ResolveX(string variableName, ResolveTargetKind kind, bool isByRef, bool isValueType) => variableName;
        
        public abstract ResolvedType Resolve(string typeName);
        public abstract ResolvedType Resolve(ITypeSymbol type);
        public abstract ResolvedType ResolvePredefinedType(ITypeSymbol type);

        public abstract ResolvedType MakeArrayType(ITypeSymbol elementType, ResolveTargetKind resolveTargetKind);

        protected abstract ResolvedType MakePointerType(ITypeSymbol pointerType);

        protected abstract ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer);
        
        private ResolvedType ResolvePredefinedAndComposedTypes(ITypeSymbol type, ResolveTargetKind resolveTargetKind)
        {
            if (type is IArrayTypeSymbol array)
            {
                return MakeArrayType(array.ElementType, resolveTargetKind);
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return MakePointerType(pointerType.PointedAtType);
            }

            if (type is IFunctionPointerTypeSymbol functionPointer)
            {
                return MakeFunctionPointerType(functionPointer);
            }
            
            if (type.SpecialType == SpecialType.None 
                || type.SpecialType == SpecialType.System_Array 
                || type.SpecialType == SpecialType.System_Enum 
                || type.SpecialType == SpecialType.System_ValueType 
                || type.SpecialType == SpecialType.System_Decimal 
                || type.SpecialType == SpecialType.System_DateTime
                || type.SpecialType == SpecialType.System_Delegate
                || type.SpecialType == SpecialType.System_MulticastDelegate
                || type.SpecialType == SpecialType.System_AsyncCallback
                || type.TypeKind == TypeKind.Interface)
            {
                return null;
            }

            return ResolvePredefinedType(type);
        }

        private ResolvedType ResolveTypeParameter(ITypeSymbol type, string cecilTypeParameterProviderVar)
        {
            if (type is not ITypeParameterSymbol typeParameterSymbol)
                return null;

            if (cecilTypeParameterProviderVar == null)
                return null;
            
            var resolvedType = typeParameterSymbol.ContainingSymbol.Kind switch
            {
                SymbolKind.NamedType => $"(({cecilTypeParameterProviderVar} is MethodReference methodReference) ? ((GenericInstanceType) methodReference.DeclaringType).ElementType : (IGenericParameterProvider) {cecilTypeParameterProviderVar} ).GenericParameters[{typeParameterSymbol.Ordinal}]",
                SymbolKind.Method => $"{cecilTypeParameterProviderVar}.GenericParameters[{typeParameterSymbol.Ordinal}]",
                _ => null
            };

            return new ResolvedType(resolvedType);
        }
        
        private ResolvedType ResolveGenericType(ITypeSymbol type, string cecilTypeParameterProviderVar)
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

        public ResolvedType ResolveLocalVariableType(ITypeSymbol type)
        {
            var containingSymbolName = type.ContainingSymbol?.OriginalDefinition.ToDisplayString();
            var found = _context.DefinitionVariables.GetVariable(type.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, containingSymbolName).VariableName
                        ?? _context.DefinitionVariables.GetVariable(type.OriginalDefinition.ToDisplayString(), VariableMemberKind.TypeParameter, containingSymbolName).VariableName;

            if (found != null && type is INamedTypeSymbol { IsGenericType: true } genericTypeSymbol)
            {
                return MakeGenericInstanceType(found, genericTypeSymbol, found);
            }

            return new ResolvedType(found);
        }

        private IList<ResolvedType> CollectTypeArguments(INamedTypeSymbol typeArgumentProvider, List<ResolvedType> collectTo, string cecilTypeParameterProviderVar)
        {
            if (typeArgumentProvider.ContainingType != null)
            {
                CollectTypeArguments(typeArgumentProvider.ContainingType, collectTo, cecilTypeParameterProviderVar);
            }
            collectTo.AddRange(typeArgumentProvider.TypeArguments.Where(t => t.Kind != SymbolKind.ErrorType).Select(t => ResolveAny(t, ResolveTargetKind.None, cecilTypeParameterProviderVar)));

            return collectTo;
        }

        private ResolvedType MakeGenericInstanceType(ResolvedType typeReference, INamedTypeSymbol genericTypeSymbol, string cecilTypeParameterProviderVar)
        {
            var typeArgs = CollectTypeArguments(genericTypeSymbol, [], cecilTypeParameterProviderVar);
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
