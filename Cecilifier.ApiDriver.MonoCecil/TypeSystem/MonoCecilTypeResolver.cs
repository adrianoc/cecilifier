using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil.TypeSystem;

public class MonoCecilTypeResolver(MonoCecilContext context) : TypeResolverBase<MonoCecilContext>(context)
{
    public override string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");
    public override string Resolve(ITypeSymbol type) => Resolve($"""{type.ToDisplayString()}""");
    
    public override string ResolvePredefinedType(ITypeSymbol type) => $"assembly.MainModule.TypeSystem.{type.Name}";
    public override string MakeArrayType(ITypeSymbol elementType) => ResolveAny(elementType) + ".MakeArrayType()";
    protected override string MakePointerType(ITypeSymbol pointerType) => ResolveAny(pointerType) + ".MakePointerType()";

    protected override string MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer)
    {
        return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
    }
}
