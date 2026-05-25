using System.Collections.Immutable;
using Cecilifier.Core;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataTypeResolver(SystemReflectionMetadataContext context) : TypeResolverBase<SystemReflectionMetadataContext>(context)
{
    public override ResolvedType Resolve(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        
        var memberRefVar = _context.DefinitionVariables.GetVariable(type.ToDisplayString(), VariableMemberKind.Type, type.ContainingSymbol.ToDisplayString());
        var memberRefVarName = memberRefVar.IsValid 
                                        ? memberRefVar.VariableName
                                        : _context.Naming.SyntheticVariable(type.ToValidVariableName(), ElementKind.MemberReference);
        if (!memberRefVar.IsValid)
        {
            _context.DefinitionVariables.RegisterNonMethod(type.ContainingSymbol.ToDisplayString(), type.ToDisplayString(), VariableMemberKind.Type, memberRefVarName);
        }
        
        
        var assemblyReferenceName = _context.AssemblyResolver.Resolve(_context, type.ContainingAssembly);
        _context.Generate($"""
                           var {memberRefVarName} = metadata.AddTypeReference(
                                                                {assemblyReferenceName},
                                                                metadata.GetOrAddString("{type.ContainingNamespace.FullyQualifiedName()}"),
                                                                metadata.GetOrAddString("{type.Name}{GenericRankAnnotation(type)}"));
                           """);
        _context.WriteNewLine();

        if (resolutionContext.TargetKind == ResolveTargetKind.TypeReference)
            return memberRefVarName;
        
        return ApplySpecificSyntax(memberRefVarName, in resolutionContext);
    }

    private string GenericRankAnnotation(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true}  namedType)
            return $"`{namedType.TypeArguments.Length}";
        
        return string.Empty;
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
        {
            var mangledTypeVariableName = $"resolved-type$=>{type.ToDisplayString()}";
            var found = _context.DefinitionVariables.GetVariable(mangledTypeVariableName, VariableMemberKind.Type, type.ContainingAssembly.ToDisplayString());
            if (found.IsValid)
                return found.VariableName;
            
            var resolvedTypeVariable = _context.Naming.SyntheticVariable(type.Name, ElementKind.MemberReference);
            _context.DefinitionVariables.RegisterNonMethod(type.ContainingAssembly.ToDisplayString(), mangledTypeVariableName, VariableMemberKind.Type, resolvedTypeVariable);
            
            _context.Generate($"""
                     var {resolvedTypeVariable} = metadata.AddTypeReference({_context.AssemblyResolver.Resolve(_context, _context.RoslynTypeSystem.SystemObject.ContainingAssembly)}, metadata.GetOrAddString("{type.ContainingNamespace.Name}"), metadata.GetOrAddString("{type.Name}"));
                     """);
            
            _context.WriteNewLine();
            return resolvedTypeVariable;
        }
        return ResolveForTargetKind(type, resolutionContext);
    }

    public override ResolvedType ResolveLocalVariableType(ITypeSymbol type, in TypeResolutionContext context)
    {
        var resolved = base.ResolveLocalVariableType(type, in context);
        if (resolved && context.TargetKind != ResolveTargetKind.TypeReference)
        {
            var methodBuilder = context.TargetKind == ResolveTargetKind.GenericTypeArgument 
                ? $"GenericTypeParameter({resolved.Expression})" 
                : $"Type({resolved.Expression}, isValueType: {context.Options.HasFlag(TypeResolutionOptions.IsValueType).ToKeyword()})";

            if ((context.TargetKind == ResolveTargetKind.Field || context.TargetKind == ResolveTargetKind.Parameter || context.TargetKind == ResolveTargetKind.ReturnType) && type is INamedTypeSymbol { IsGenericType: true })
            {
                methodBuilder = resolved.Expression;
            }
            
            return ResolvedType.FromDetails(
                new ResolvedTypeDetails()
                    .WithTypeEncoder(TypeEncoderFor(in context))
                    .WithMethodBuilder(methodBuilder));

        }
        return resolved;
    }

    public override ResolvedType MakeGenericInstanceType(ResolvedType typeReference, INamedTypeSymbol genericTypeSymbol, in TypeResolutionContext resolutionContext)
    {
        Buffer256<ITypeSymbol> typeArgumentsBuffer = new();
        ReadOnlySpan<ITypeSymbol> typeArguments = CollectTypeArguments(genericTypeSymbol, ref typeArgumentsBuffer);
        
        if (typeArguments.Length == 0)
            return typeReference;

        if (resolutionContext.TargetKind == ResolveTargetKind.Field || resolutionContext.TargetKind == ResolveTargetKind.Parameter || resolutionContext.TargetKind == ResolveTargetKind.ReturnType)
        {
            return MakeGenericInstanceTypeForFieldDeclaration(typeReference, genericTypeSymbol, typeArguments);
        }
        
        var genericInstanceTypeVar = context.Naming.SyntheticVariable($"{genericTypeSymbol.ToValidVariableName()}Instantiation", ElementKind.GenericInstance);
        context.Generate($$"""
                           TypeSpecificationHandle {{genericInstanceTypeVar}} = default;
                           {
                               var be = new BlobEncoder(new BlobBuilder());
                               var typeSpecificationSig = be.TypeSpecificationSignature();
                               var gti = typeSpecificationSig.GenericInstantiation({{typeReference.Expression}}, {{typeArguments.Length}}, isValueType: {{genericTypeSymbol.IsValueType.ToKeyword()}});    
                               {{
                                   typeArguments.ToImmutableArray().Select(
                                           targ => $"gti.AddArgument().{context.TypedTypeResolver.ResolveAny(targ, ResolveTargetKind.GenericTypeArgument)};\n")
                                       .Aggregate("", (acc, s) => acc + s)
                               }}
                               {{genericInstanceTypeVar}} = metadata.AddTypeSpecification(metadata.GetOrAddBlob(be.Builder));
                           }
                           """);
        context.WriteNewLine();
        
        return genericInstanceTypeVar;
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
            ResolveTargetKind.GenericTypeArgument => "",
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
    
    private ResolvedType MakeGenericInstanceTypeForFieldDeclaration(ResolvedType typeReference, INamedTypeSymbol genericTypeSymbol, ReadOnlySpan<ITypeSymbol> typeArguments)
    {
        var ret = $$"""
                    WithSignatureTypeEncoder(typeSignatureEncoder => 
                    {
                        var gi = typeSignatureEncoder.GenericInstantiation({{typeReference.Expression}}, {{typeArguments.Length}}, isValueType: {{genericTypeSymbol.IsValueType.ToKeyword()}});
                        {{
                            typeArguments.ToImmutableArray().Select(
                                    targ => $"gi.AddArgument().{context.TypedTypeResolver.ResolveAny(targ, ResolveTargetKind.GenericTypeArgument)};\n    ")
                                .Aggregate("", (acc, s) => acc + s)
                        }}
                    })
                    """;
        return ret;
    }
}
