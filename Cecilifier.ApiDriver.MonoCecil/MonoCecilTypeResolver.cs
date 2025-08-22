using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilTypeResolver(IVisitorContext context) : TypeResolverBase(context)
{
    public override string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");
    public override string ResolvePredefinedType(ITypeSymbol type) => $"assembly.MainModule.TypeSystem.{type.Name}";
    protected override string ResolveArrayType(IArrayTypeSymbol array) => Resolve(array.ElementType) + ".MakeArrayType()";
    protected override string MakePointerType(IPointerTypeSymbol pointerType) => Resolve(pointerType.PointedAtType) + ".MakePointerType()";

    protected override string MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer)
    {
        return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
    }
}
