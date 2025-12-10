using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilTypeResolver(MonoCecilContext context) : TypeResolverBase<MonoCecilContext>(context)
{
    public override ResolvedType Resolve(string typeName, in TypeResolutionContext resolutionContext) => Utils.ImportFromMainModule($"typeof({typeName})");
    public override ResolvedType Resolve(ITypeSymbol type, in TypeResolutionContext resolutionContext) => Resolve($"""{type.ToDisplayString()}""", in resolutionContext);
    
    public override ResolvedType ResolvePredefinedType(ITypeSymbol type, in TypeResolutionContext resolutionContext) => $"assembly.MainModule.TypeSystem.{type.Name}";
    public override ResolvedType MakeArrayType(ITypeSymbol elementType, in TypeResolutionContext resolutionContext) => ResolveAny(elementType, in resolutionContext) + ".MakeArrayType()";
    protected override ResolvedType MakePointerType(ITypeSymbol pointerType, in TypeResolutionContext resolutionContext) => ResolveAny(pointerType, in resolutionContext) + ".MakePointerType()";

    protected override ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer, in TypeResolutionContext resolutionContext)
    {
        return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
    }
}
