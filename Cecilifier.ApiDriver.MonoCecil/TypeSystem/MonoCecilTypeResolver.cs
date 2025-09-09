using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilTypeResolver(MonoCecilContext context) : TypeResolverBase<MonoCecilContext>(context)
{
    public override string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");
    public override string Resolve(ITypeSymbol type) => Resolve(type.ToDisplayString());

    public override string ResolvePredefinedType(ITypeSymbol type) => $"assembly.MainModule.TypeSystem.{type.Name}";
    protected override string ResolveArrayType(IArrayTypeSymbol array) => ResolveAny(array.ElementType) + ".MakeArrayType()";
    protected override string MakePointerType(IPointerTypeSymbol pointerType) => ResolveAny(pointerType.PointedAtType) + ".MakePointerType()";

    protected override string MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer)
    {
        return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
    }
}
