using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

//TODO: Consider having 2 overload for each ResolveX() method.
//        - One returning an expression (exactly as of original implementation)
//        - A seconde one returning a list of expressions to allow for instance, to register new variable definitions
//          to store intermediate values (for instance, when resolving types in general
//          it may be required to introduce an assembly reference. If the code can only return expressions
//          it becomes hard (if not impossible) to store this reference in a variable to be used in a future
//          call to ResolveX()
public class SystemReflectionMetadataTypeResolver(IVisitorContext context) : TypeResolverBase(context)
{
    public override string Resolve(string typeName) => $"TODO: Fix Resolve(\"{typeName}\")";

    public override string ResolvePredefinedType(ITypeSymbol type)
    {
        return $"""
                metadata.AddTypeReference(
                             mscorlibAssemblyRef,
                             metadata.GetOrAddString("{type.ContainingNamespace.Name}"),
                             metadata.GetOrAddString("{type.Name}"))
                """;
    }

    protected override string ResolveArrayType(IArrayTypeSymbol type)
    {
        throw new NotImplementedException();
    }

    protected override string MakePointerType(IPointerTypeSymbol pointerType)
    {
        throw new NotImplementedException();
    }

    protected override string MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer)
    {
        throw new NotImplementedException();
    }
}
