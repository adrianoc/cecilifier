using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataTypeResolver(SystemReflectionMetadataContext context) : TypeResolverBase<SystemReflectionMetadataContext>(context)
{
    public override ResolvedType Resolve(string typeName, in TypeResolutionContext resolutionContext) => throw new NotSupportedException($"{typeName} (context: {resolutionContext})");
    
    public override ResolvedType Resolve(ITypeSymbol type, in TypeResolutionContext resolutionContext)
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

        if (resolutionContext.TargetKind == ResolveTargetKind.TypeReference)
            return memberRefVarName;
        
        return ApplySpecificSyntax(memberRefVarName, in resolutionContext);
    }

    /// <summary>
    /// Returns an expression that is suitable to be used with Parameter/Locals/Field/ReturnTypeEncoder
    /// </summary>
    /// <returns></returns>
    private ResolvedType ResolveForTargetKind(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return "Void()";
        }

        var resolvedTypeDetails = new ResolvedTypeDetails();
        if (type.IsPrimitiveType() || type.SpecialType == SpecialType.System_String || type.SpecialType == SpecialType.System_Object || type.SpecialType == SpecialType.System_IntPtr)
            return ResolvedType.FromDetails(
                            resolvedTypeDetails
                                .WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                                .WithMethodBuilder($"{type.MetadataName}()"));

        return ResolvedType.FromDetails(
            resolvedTypeDetails
                .WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                .WithMethodBuilder($"Type({ResolveAny(type, TypeResolution.DefaultContext)}, isValueType: {type.IsValueType.ToKeyword()})"));
    }

    public override ResolvedType ApplySpecificSyntax(string variableName, in TypeResolutionContext resolutionContext)
    {
        var resolvedTypeDetails = new ResolvedTypeDetails();
        return ResolvedType.FromDetails(
            resolvedTypeDetails
                .WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                .WithMethodBuilder($"Type({variableName}, isValueType: { ((resolutionContext.Options & TypeResolutionOptions.IsValueType) == TypeResolutionOptions.IsValueType).ToKeyword()})"));
    }

    public override ResolvedType ResolvePredefinedType(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        if (resolutionContext.TargetKind == ResolveTargetKind.TypeReference)
            return $"""
                      metadata.AddTypeReference(
                          {_context.AssemblyResolver.Resolve(_context, _context.RoslynTypeSystem.SystemObject.ContainingAssembly)},
                          metadata.GetOrAddString("{type.ContainingNamespace.Name}"),
                          metadata.GetOrAddString("{type.Name}"))
                      """;
        return ResolveForTargetKind(type, resolutionContext);
    }

    public override ResolvedType ResolveLocalVariableType(ITypeSymbol type, in TypeResolutionContext context)
    {
        var resolved = base.ResolveLocalVariableType(type, in context);
        if (resolved && context.TargetKind != ResolveTargetKind.TypeReference)
        {
            return ResolvedType.FromDetails(
                new ResolvedTypeDetails()
                    .WithTypeEncoder(TypeEncoderFor(in context))
                    .WithMethodBuilder($"Type({resolved.Expression}, isValueType: {context.Options.HasFlag(TypeResolutionOptions.IsValueType).ToKeyword()})"));

        }
        return resolved;
    }

    public override ResolvedType MakeArrayType(ITypeSymbol elementType, in TypeResolutionContext resolutionContext)
    {
        var details = new ResolvedTypeDetails();
        var methodBuilderByKind = resolutionContext.TargetKind == ResolveTargetKind.AttributeNamedArgument ? "SZArray().ElementType()" : "SZArray()";
        if (resolutionContext.TargetKind == ResolveTargetKind.AttributeNamedArgument && elementType.TypeKind == TypeKind.Enum)
        {
            var enumDeclaringAssembly = elementType.IsDefinedInCurrentAssembly(_context) ? string.Empty : $",{elementType.ContainingAssembly.ToDisplayString()}";
            return ResolvedType.FromDetails(
                details.WithMethodBuilder($"""{methodBuilderByKind}.Enum("{elementType.ToDisplayString()}{enumDeclaringAssembly}")"""));
            
        }
        
        return ResolvedType.FromDetails(
                    details.WithTypeEncoder(TypeEncoderForArrayElement(in resolutionContext))
                        .WithMethodBuilder($"{methodBuilderByKind}.{ResolveAny(elementType, new TypeResolutionContext(ResolveTargetKind.ArrayElementType, resolutionContext.Options))}"));
    }

    protected override ResolvedType MakePointerType(ITypeSymbol pointerType, in TypeResolutionContext resolutionContext)
    {
        throw new NotImplementedException();
    }

    protected override ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer, in TypeResolutionContext resolutionContext)
    {
        throw new NotImplementedException();
    }
    
    private static string TypeEncoderFor(in TypeResolutionContext resolutionContext)
    {
        if (resolutionContext.TargetKind == ResolveTargetKind.Instruction)
            return "TokenForType(enc => enc%, metadata)";
        
        var isByRef = ((resolutionContext.Options & TypeResolutionOptions.IsByRef) == TypeResolutionOptions.IsByRef).ToKeyword();
        return resolutionContext.TargetKind switch
        {
            ResolveTargetKind.None => "",
            ResolveTargetKind.ArrayElementType => "",
            ResolveTargetKind.AttributeNamedArgument or ResolveTargetKind.AttributeArgument => "ScalarType()%",
            _ => $"Type(isByRef: {isByRef})%",
        };
    }
    
    private static string TypeEncoderForArrayElement(in TypeResolutionContext resolutionContext)
    {
        if (resolutionContext.TargetKind == ResolveTargetKind.Instruction)
            return "TokenForType(enc => enc%, metadata)";
        
        var isByRef = ((resolutionContext.Options & TypeResolutionOptions.IsByRef) == TypeResolutionOptions.IsByRef).ToKeyword();
        return resolutionContext.TargetKind switch
        {
            ResolveTargetKind.AttributeNamedArgument => "",
            ResolveTargetKind.None => "",
            ResolveTargetKind.ArrayElementType => "",
            _ => $"Type(isByRef: {isByRef})%",
        };
    }
}
