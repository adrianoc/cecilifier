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
    protected override ResolvedType ResolveTypeParameter(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        if (type is not ITypeParameterSymbol typeParameterSymbol)
            return null;

        return ResolvedType.FromDetails(
            new ResolvedTypeDetails()
                .WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                .WithMethodBuilder(GenericParameterExpressionFor(typeParameterSymbol)));
    }
    
    public override ResolvedType ResolveTypeParameter(ResolvedType genericTypeParameter, TypeParameterKind typeParameterKind, in TypeResolutionContext resolutionContext)
    {
        return ResolvedType.FromDetails(
            new ResolvedTypeDetails()
                .WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                .WithMethodBuilder(GenericParameterExpressionFor(genericTypeParameter, typeParameterKind)));
    }

    protected override ResolvedType ResolveFromAssembly(ITypeSymbol type, in TypeResolutionContext resolutionContext)
    {
        var memberRefVarName = _context.Naming.SyntheticVariable(type.ToValidVariableName(), ElementKind.MemberReference);
        
        var assemblyReferenceName = _context.AssemblyResolver.Resolve(_context, type.ContainingAssembly);
        _context.Generate($"""
                           var {memberRefVarName} = metadata.AddTypeReference(
                                                                {assemblyReferenceName},
                                                                metadata.GetOrAddString("{type.ContainingNamespace.FullyQualifiedName()}"),
                                                                metadata.GetOrAddString("{type.Name}{GenericRankAnnotation(type)}"));
                           """);
        _context.WriteNewLine();

        RegisterVariableIfNeeded(type, memberRefVarName, in resolutionContext);
        
        if (resolutionContext.TargetKind is ResolveTargetKind.TypeReference or ResolveTargetKind.GenericTypeParameterConstraint || type is INamedTypeSymbol { IsGenericType: true })
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
                .WithMethodBuilder($"Type({Resolve(type, TypeResolution.DefaultContext)}, isValueType: {type.IsValueType.ToKeyword()})"));
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
          if (resolutionContext.TargetKind != ResolveTargetKind.TypeReference)
              return ResolveForTargetKind(type, resolutionContext);
              
          var mangledTypeVariableName = $"resolved-type$=>{type.ToDisplayString()}";
          var found = _context.DefinitionVariables.GetVariable(mangledTypeVariableName, VariableMemberKind.Type, type.ContainingAssembly.ToDisplayString());
          if (found.IsValid)
              return found.VariableName;
              
          var resolvedTypeVariable = _context.Naming.SyntheticVariable(type.Name, ElementKind.MemberReference);
          _context.DefinitionVariables.RegisterNonMethod(type.ContainingAssembly.ToDisplayString(), mangledTypeVariableName, VariableMemberKind.Type, resolvedTypeVariable);
              
          _context.Generate($"""
                             var {resolvedTypeVariable} = metadata.AddTypeReference({_context.AssemblyResolver.Resolve(_context, _context.RoslynTypeSystem.SystemObject.ContainingAssembly)}, metadata.GetOrAddString("{type.ContainingNamespace.Name}"), metadata.GetOrAddString("{type.MetadataName}"));
                             """);
              
          _context.WriteNewLine();
          return resolvedTypeVariable;
    }

    public override ResolvedType ResolveLocalVariableType(ITypeSymbol type, in TypeResolutionContext context)
    {
        var resolved = base.ResolveLocalVariableType(type, in context);
        if (!resolved)
            return resolved;

        if (context.TargetKind == ResolveTargetKind.Instruction)
        {
            return new ResolvedType($"MetadataTokens.GetToken({resolved})");
        }

        if (type.TypeKind == TypeKind.TypeParameter && context.TargetKind is ResolveTargetKind.GenericTypeParameterConstraint or ResolveTargetKind.TypeReference)
        {
            //TODO: Try to register/lookup a variable for the type parameter type specification to avoid duplication.
            var typeParameterBlobEncoderVar = _context.Naming.SyntheticVariable(type.Name, ElementKind.GenericParameter);
            var typeParameterTypeSpecificationVar = _context.Naming.SyntheticVariable(type.Name, ElementKind.GenericParameter);
            
            _context.Generate(StringExtensions.Indented($"""
                                                         var {typeParameterBlobEncoderVar} = new BlobEncoder(new BlobBuilder());
                                                         {typeParameterBlobEncoderVar}.TypeSpecificationSignature().{GenericParameterExpressionFor((ITypeParameterSymbol) type)};
                                                         TypeSpecificationHandle {typeParameterTypeSpecificationVar} = metadata.AddTypeSpecification(metadata.GetOrAddBlob({typeParameterBlobEncoderVar}.Builder));
                                                         """));

            _context.WriteNewLine();
            return new ResolvedType(typeParameterTypeSpecificationVar);
        }
        
        if (context.TargetKind != ResolveTargetKind.GenericTypeArgument || type is not INamedTypeSymbol { IsGenericType: true })
            if (context.TargetKind != ResolveTargetKind.TypeReference && ((context.TargetKind != ResolveTargetKind.Field && context.TargetKind != ResolveTargetKind.ReturnType && context.TargetKind != ResolveTargetKind.LocalVariable) || type is not INamedTypeSymbol { IsGenericType: true }))
            {
                var methodBuilder = type.TypeKind == TypeKind.TypeParameter
                    ? GenericParameterExpressionFor((ITypeParameterSymbol) type)
                    : $"Type({resolved.Expression}, isValueType: {context.Options.HasFlag(TypeResolutionOptions.IsValueType).ToKeyword()})";

                if (context.TargetKind is ResolveTargetKind.GenericTypeParameterConstraint || (context.TargetKind is ResolveTargetKind.Field or ResolveTargetKind.Parameter && type is INamedTypeSymbol { IsGenericType: true }))
                {
                    return resolved.Expression;
                }
            
                return ResolvedType.FromDetails(
                    new ResolvedTypeDetails()
                        .WithTypeEncoder(TypeEncoderFor(in context))
                        .WithMethodBuilder(methodBuilder));
            }
        
        return resolved;
    }

    public override ResolvedType MakeGenericInstanceType(string typeName, ResolvedType openGenericType, INamedTypeSymbol genericTypeSymbol, in TypeResolutionContext resolutionContext)
    {
        Buffer256<ITypeSymbol> typeArgumentsBuffer = new();
        ReadOnlySpan<ITypeSymbol> typeArguments = CollectTypeArguments(genericTypeSymbol, ref typeArgumentsBuffer);
        
        if (typeArguments.Length == 0)
            return openGenericType;

        if (resolutionContext.TargetKind is ResolveTargetKind.Field or ResolveTargetKind.Parameter or ResolveTargetKind.ReturnType or ResolveTargetKind.LocalVariable or ResolveTargetKind.GenericTypeArgument)
        {
            var resolved = MakeGenericInstanceType(openGenericType, genericTypeSymbol, typeArguments);
            if (resolved && resolutionContext.TargetKind is ResolveTargetKind.ReturnType or ResolveTargetKind.Field or ResolveTargetKind.LocalVariable or ResolveTargetKind.Parameter)
            {
                return ResolvedType.FromDetails(
                    new ResolvedTypeDetails()
                        .WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                        .WithMethodBuilder(resolved.Expression));
            }

            return resolved;
        }

        var definitionVariable = _context.DefinitionVariables.GetOrRegisterNonMethodVariable(typeName, string.Empty, VariableMemberKind.None,
            new TypeResolutionContext(ResolveTargetKind.None, TypeResolutionOptions.RegisterVariables),
            typeArguments,
            state =>
            {
                var genericInstanceTypeVar = _context.Naming.SyntheticVariable($"{genericTypeSymbol.ToValidVariableName()}Instantiation", ElementKind.GenericInstance);
                _context.Generate($$"""
                                   TypeSpecificationHandle {{genericInstanceTypeVar}} = default;
                                   {
                                       var typeSpecificationSig = new BlobEncoder(new BlobBuilder()).TypeSpecificationSignature();
                                       var gti = typeSpecificationSig.GenericInstantiation({{openGenericType.Expression}}, {{state.Length}}, isValueType: {{genericTypeSymbol.IsValueType.ToKeyword()}});    
                                       {{
                                           state.ToImmutableArray().Select(
                                                   targ => $"gti.AddArgument().{_context.TypedTypeResolver.Resolve(targ, ResolveTargetKind.GenericTypeArgument)};\n")
                                               .Aggregate("", (acc, s) => acc + s)
                                       }}
                                       {{genericInstanceTypeVar}} = metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecificationSig.Builder));
                                   }
                                   """);
                _context.WriteNewLine();

                return genericInstanceTypeVar;
            });
        
        definitionVariable.ThrowIfVariableIsNotValid();
        return definitionVariable.VariableName;
    }

    public override ResolvedType MakeGenericInstanceType(string typeName, ResolvedType openGenericType, Span<ResolvedType> typeArguments, in TypeResolutionContext resolutionContext)
    {
        if (typeArguments.Length == 0)
            return openGenericType;

        var isValueType = resolutionContext.Options.HasFlag(TypeResolutionOptions.IsValueType);
        if (resolutionContext.TargetKind is ResolveTargetKind.Field or ResolveTargetKind.Parameter or ResolveTargetKind.ReturnType or ResolveTargetKind.LocalVariable or ResolveTargetKind.GenericTypeArgument)
        {
            var resolved = MakeGenericInstanceType(openGenericType,  isValueType: isValueType, typeArguments);
            if (resolved && resolutionContext.TargetKind is ResolveTargetKind.ReturnType or ResolveTargetKind.Field or ResolveTargetKind.LocalVariable or ResolveTargetKind.Parameter)
            {
                return ResolvedType.FromDetails(
                    new ResolvedTypeDetails()
                        .WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                        .WithMethodBuilder(resolved.Expression));
            }

            return resolved;
        }

        var immutableArray = typeArguments.ToImmutableArray();
        var definitionVariable = _context.DefinitionVariables.GetOrRegisterNonMethodVariable(
            typeName, 
            string.Empty, 
            VariableMemberKind.None, 
            new TypeResolutionContext(ResolveTargetKind.None, TypeResolutionOptions.RegisterVariables),
            string.Empty,
            _ =>
        {
            var genericInstanceTypeVar = _context.Naming.SyntheticVariable("Instantiation", ElementKind.GenericInstance);
            _context.Generate($$"""
                               TypeSpecificationHandle {{genericInstanceTypeVar}} = default;
                               {
                                   var typeSpecificationSig = new BlobEncoder(new BlobBuilder()).TypeSpecificationSignature();
                                   var gti = typeSpecificationSig.GenericInstantiation({{openGenericType.Expression}}, {{immutableArray.Length}}, isValueType: {{isValueType.ToKeyword()}});    
                                   {{
                                       immutableArray.Select(targ => $"gti.AddArgument().{targ.ToString()};\n").Aggregate("", (acc, s) => acc + s)
                                   }}
                                   {{genericInstanceTypeVar}} = metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecificationSig.Builder));
                               }
                               """);
            _context.WriteNewLine();

            return genericInstanceTypeVar;
        });

        definitionVariable.ThrowIfVariableIsNotValid();
        return definitionVariable.VariableName;
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
                        .WithMethodBuilder($"{methodBuilderByKind}.{Resolve(elementType, new TypeResolutionContext(ResolveTargetKind.ComposedElementType, resolutionContext.Options))}"));
    }

    protected override ResolvedType MakePointerType(ITypeSymbol pointerType, in TypeResolutionContext resolutionContext)
    {
        var details = new ResolvedTypeDetails();
        return ResolvedType.FromDetails(
            details.WithTypeEncoder(TypeEncoderFor(in resolutionContext))
                .WithMethodBuilder($"Pointer().{Resolve(pointerType, new TypeResolutionContext(ResolveTargetKind.ComposedElementType, resolutionContext.Options))}"));
    }

    protected override ResolvedType MakeFunctionPointerType(IFunctionPointerTypeSymbol functionPointer, in TypeResolutionContext resolutionContext)
    {
        throw new NotImplementedException();
    }
    
    public override ResolvedType MakeByRefType(in ResolvedType resolvedType) => resolvedType; // noop in SRM. ByRef types are handled during type resolution.
    
    private static string TypeEncoderFor(in TypeResolutionContext resolutionContext)
    {
        if (resolutionContext.TargetKind == ResolveTargetKind.Instruction)
            return "TokenForType(enc => enc%, metadata)";
        
        var isByRef = ((resolutionContext.Options & TypeResolutionOptions.IsByRef) == TypeResolutionOptions.IsByRef).ToKeyword();
        return resolutionContext.TargetKind switch
        {
            ResolveTargetKind.None => "",
            ResolveTargetKind.GenericTypeArgument => "",
            ResolveTargetKind.GenericTypeParameterConstraint => "",
            ResolveTargetKind.ComposedElementType => "",
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
            ResolveTargetKind.ComposedElementType => "",
            _ => $"Type(isByRef: {isByRef})%",
        };
    }
    
    private ResolvedType MakeGenericInstanceType(ResolvedType openGenericType, INamedTypeSymbol genericTypeSymbol, ReadOnlySpan<ITypeSymbol> typeArguments)
    {
        var ret = $$"""
                    WithSignatureTypeEncoder(typeSignatureEncoder =>
                    {
                        var gi = typeSignatureEncoder.GenericInstantiation({{openGenericType.Expression}}, {{typeArguments.Length}}, isValueType: {{genericTypeSymbol.IsValueType.ToKeyword()}});
                        {{
                            typeArguments.ToImmutableArray().Select(
                                    targ => $"gi.AddArgument().{_context.TypedTypeResolver.Resolve(targ, ResolveTargetKind.GenericTypeArgument)};\n    ")
                                .Aggregate("", (acc, s) => acc + s)
                        }}
                    })
                    """;
        return ret;
    }
    
    private ResolvedType MakeGenericInstanceType(ResolvedType openGenericType, bool isValueType, ReadOnlySpan<ResolvedType> typeArguments)
    {
        var ret = $$"""
                    WithSignatureTypeEncoder(typeSignatureEncoder =>
                    {
                        var gi = typeSignatureEncoder.GenericInstantiation({{openGenericType.Expression}}, {{typeArguments.Length}}, isValueType: {{isValueType.ToKeyword()}});
                        {{
                            typeArguments.ToImmutableArray().Select(targ => $"gi.AddArgument().{targ};\n    ").Aggregate("", (acc, s) => acc + s)
                        }}
                    })
                    """;
        return ret;
    }
    
    private void RegisterVariableIfNeeded(ITypeSymbol type, string variableName, in TypeResolutionContext resolutionContext)
    {
        if (!resolutionContext.Options.HasFlag(TypeResolutionOptions.RegisterVariables))
            return;
        
        _context.DefinitionVariables.RegisterNonMethod(type.ContainingSymbol.OriginalDefinition.ToDisplayString(), type.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, variableName);
    }
    
    private string GenericParameterExpressionFor(ITypeParameterSymbol typeParameter) => typeParameter.TypeParameterKind == TypeParameterKind.Type
        ? $"GenericTypeParameter({typeParameter.Ordinal})"
        : $"GenericMethodTypeParameter({typeParameter.Ordinal})";
    
    private string GenericParameterExpressionFor(ResolvedType genericTypeParameter, TypeParameterKind typeParameterKind)=> typeParameterKind == TypeParameterKind.Type
        ? $"GenericTypeParameter({genericTypeParameter})"
        : $"GenericMethodTypeParameter({genericTypeParameter})";
}
