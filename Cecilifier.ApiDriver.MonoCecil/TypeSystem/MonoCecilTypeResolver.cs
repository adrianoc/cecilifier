using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilTypeResolver(MonoCecilContext context) : TypeResolverBase<MonoCecilContext>(context)
{
    public override ResolvedType Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");
    public override ResolvedType Resolve(ITypeSymbol type) => Resolve($"""{type.ToDisplayString()}""");
    
    public override ResolvedType ResolvePredefinedType(ITypeSymbol type) => $"assembly.MainModule.TypeSystem.{type.Name}";
    public override ResolvedType MakeArrayType(ITypeSymbol elementType) => ResolveAny(elementType) + ".MakeArrayType()";
    protected override ResolvedType MakePointerType(ITypeSymbol pointerType) => ResolveAny(pointerType) + ".MakePointerType()";

    protected override ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer)
    {
        return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
    }
}
