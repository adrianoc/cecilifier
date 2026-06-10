using System.Collections.Immutable;
using Cecilifier.Core;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilTypeResolver(MonoCecilContext context) : TypeResolverBase<MonoCecilContext>(context)
{
    protected override ResolvedType ResolveTypeParameter(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        if (type is not ITypeParameterSymbol typeParameterSymbol)
            return null;

        if (resolutionContext.TypeParameterProviderVar == null)
            return null;
            
        var resolvedType = typeParameterSymbol.ContainingSymbol.Kind switch
        {
            SymbolKind.NamedType => $"(({resolutionContext.TypeParameterProviderVar} is MethodReference methodReference) ? ((GenericInstanceType) methodReference.DeclaringType).ElementType : (IGenericParameterProvider) {resolutionContext.TypeParameterProviderVar} ).GenericParameters[{typeParameterSymbol.Ordinal}]",
            SymbolKind.Method => $"{resolutionContext.TypeParameterProviderVar}.GenericParameters[{typeParameterSymbol.Ordinal}]",
            _ => null
        };

        return new ResolvedType(resolvedType);
    }
    
    protected override ResolvedType ResolveFromAssembly(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        if (type.ContainingType != null)
            return ImportReference($"typeof({$"""{type.ToDisplayString()}"""})");

        var formatOptions = SymbolDisplayFormat.FullyQualifiedFormat
                                        .RemoveGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)
                                        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);
        
        var nameToResolve = type.ToDisplayString(formatOptions);
        if (type is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            Span<char> commas = stackalloc char[namedType.TypeArguments.Length - 1];
            commas.Fill(',');
            
            nameToResolve = $"{nameToResolve}<{commas}>";
        }
        
        return ImportReference($"typeof({$"""{nameToResolve}"""})");
    }

    public override ResolvedType ResolvePredefinedType(ITypeSymbol type, in TypeResolutionContext resolutionContext) => $"assembly.MainModule.TypeSystem.{type.Name}";
    public override ResolvedType MakeArrayType(ITypeSymbol elementType, in TypeResolutionContext resolutionContext) => Resolve(elementType, in resolutionContext) + ".MakeArrayType()";
    protected override ResolvedType MakePointerType(ITypeSymbol pointerType, in TypeResolutionContext resolutionContext) => Resolve(pointerType, in resolutionContext) + ".MakePointerType()";

    protected override ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer, in TypeResolutionContext resolutionContext)
    {
        return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
    }

    public override ResolvedType MakeGenericInstanceType(ResolvedType typeReference, INamedTypeSymbol genericTypeSymbol, in TypeResolutionContext resolutionContext)
    {
        Buffer256<ITypeSymbol> g = new();
        var resolutionContextTypeParameterProviderVar = resolutionContext.TypeParameterProviderVar;
        var typeArgs = CollectTypeArguments(genericTypeSymbol, ref g)
                                                        .ToImmutableArray()
                                                        .Select(t => _context.TypeResolver.Resolve(t, ResolveTargetKind.TypeReference.ToTypeResolutionContext(resolutionContextTypeParameterProviderVar)))
                                                        .ToImmutableArray();
        
        return typeArgs.Length > 0 ? typeReference.MakeGenericInstanceType(typeArgs) : typeReference;
    }

    internal ResolvedType ImportReference(ResolvedType typeReference) => $"assembly.MainModule.ImportReference({typeReference})";
}
