using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
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
public class SystemReflectionMetadataTypeResolver(SystemReflectionMetadataContext context) : TypeResolverBase<SystemReflectionMetadataContext>(context)
{
    public override ResolvedType ResolveAny(ITypeSymbol type, ResolveTargetKind resolveTargetKind = ResolveTargetKind.None, string? cecilTypeParameterProviderVar = null)
    {
        return resolveTargetKind == ResolveTargetKind.None || type.TypeKind == TypeKind.Array ||  type.TypeKind == TypeKind.Pointer
            ? base.ResolveAny(type, resolveTargetKind, cecilTypeParameterProviderVar) 
            : ResolveForTargetKind(type, resolveTargetKind, false);        
    }

    public override ResolvedType Resolve(string typeName) => throw new NotSupportedException(nameof(Resolve));
    public override ResolvedType Resolve(ITypeSymbol type)
    {
        var memberRefVarName = _context.Naming.SyntheticVariable($"{type.ToValidVariableName()}", ElementKind.MemberReference);
        var assemblyReferenceName = _context.AssemblyResolver.Resolve(_context, type.ContainingAssembly);
        _context.Generate($"""
                           var {memberRefVarName} = metadata.AddTypeReference(
                                                                {assemblyReferenceName},
                                                                metadata.GetOrAddString("{type.ContainingNamespace.FullyQualifiedName()}"),
                                                                metadata.GetOrAddString("{type.Name}"));
                           """);
        _context.WriteNewLine();

        return memberRefVarName;
    }

    /// <summary>
    /// Returns an expression that is suitable to be used with Parameter/Locals/Field/ReturnTypeEncoder
    /// </summary>
    /// <param name="type"></param>
    /// <param name="kind"></param>
    /// <param name="isByRef"></param>
    /// <returns></returns>
    public ResolvedType ResolveForTargetKind(ITypeSymbol type, ResolveTargetKind kind, bool isByRef)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return "Void()";
        }
        
        if (type.IsPrimitiveType() || type.SpecialType == SpecialType.System_String || type.SpecialType == SpecialType.System_Object)
            return $"{(kind <= ResolveTargetKind.ArrayElementType ? "" : "Type().")}{type.MetadataName}()";

        return $"""{(kind <= ResolveTargetKind.ArrayElementType ? "" : $"Type(isByRef: {isByRef.ToKeyword()}).")}Type({ResolveAny(type, ResolveTargetKind.None)}, isValueType: {type.IsValueType.ToKeyword()})""";
    }

    public override ResolvedType ResolvePredefinedType(ITypeSymbol type) => $"""
                                                                             metadata.AddTypeReference(
                                                                                          {_context.AssemblyResolver.Resolve(_context, _context.RoslynTypeSystem.SystemObject.ContainingAssembly)},
                                                                                          metadata.GetOrAddString("{type.ContainingNamespace.Name}"),
                                                                                          metadata.GetOrAddString("{type.Name}"))
                                                                             """;

    public override ResolvedType MakeArrayType(ITypeSymbol elementType)
    {
        return $"Type().SZArray().{ResolveForTargetKind(elementType, ResolveTargetKind.ArrayElementType, isByRef: false)}";
    }

    protected override ResolvedType MakePointerType(ITypeSymbol pointerType)
    {
        throw new NotImplementedException();
    }

    protected override ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer)
    {
        throw new NotImplementedException();
    }
}
