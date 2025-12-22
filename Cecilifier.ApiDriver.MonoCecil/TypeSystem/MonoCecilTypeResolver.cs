using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilTypeResolver(MonoCecilContext context) : TypeResolverBase<MonoCecilContext>(context)
{
    public override ResolvedType Resolve(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        if (type.ContainingType != null)
            return Utils.ImportFromMainModule($"typeof({$"""{type.ToDisplayString()}"""})");

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
        
        return Utils.ImportFromMainModule($"typeof({$"""{nameToResolve}"""})");
    }

    public override ResolvedType ResolvePredefinedType(ITypeSymbol type, in TypeResolutionContext resolutionContext) => $"assembly.MainModule.TypeSystem.{type.Name}";
    public override ResolvedType MakeArrayType(ITypeSymbol elementType, in TypeResolutionContext resolutionContext) => ResolveAny(elementType, in resolutionContext) + ".MakeArrayType()";
    protected override ResolvedType MakePointerType(ITypeSymbol pointerType, in TypeResolutionContext resolutionContext) => ResolveAny(pointerType, in resolutionContext) + ".MakePointerType()";

    protected override ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer, in TypeResolutionContext resolutionContext)
    {
        return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
    }

    protected override ResolvedType MakeGenericInstanceType(ResolvedType typeReference, INamedTypeSymbol genericTypeSymbol, in TypeResolutionContext resolutionContext)
    {
        var typeArgs = CollectTypeArguments(genericTypeSymbol, [], resolutionContext.TypeParameterProviderVar);
        return typeArgs.Count > 0
            ? typeReference.MakeGenericInstanceType(typeArgs)
            : typeReference;
    }
}
