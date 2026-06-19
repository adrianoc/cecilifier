using System.Runtime.CompilerServices;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataMemberResolver(SystemReflectionMetadataContext context) : IMemberResolver
{
    public string ResolveMethod(IMethodSymbol method)
    {
        var methodRefVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}", ElementKind.MemberReference);
        var toBeFound = method.AsRawMethodDefinitionVariable(VariableMemberKind.MethodReference);
        var found = context.DefinitionVariables.GetMethodVariable(toBeFound);
        if (found.IsValid)
        {
            methodRefVar = found.VariableName;
        }
        else
        {
            var methodSignatureVariableToFind = method.AsMethodDefinitionVariable(VariableMemberKind.MethodSignature);

            var methodSignatureVarName = context.Naming.SyntheticVariable($"{method.Name}Signature", ElementKind.MemberReference);
            var methodSignatureVar = FindOrRegisterVariable(context, method, methodSignatureVariableToFind, methodSignatureVarName, null!, static (ctx, method, methodSignatureVarName, _) =>
            {
                var isInstanceMethod = !method.IsStatic && method.MethodKind != MethodKind.LocalFunction; // local functions are always declared as static (we don't support capturing variables)
                var methodSignatureBlobVar = ctx.Naming.SyntheticVariable($"{method.ToValidVariableName()}BlobBuilder", ElementKind.MemberReference);

                ctx.Generate($$"""
                                   var {{methodSignatureBlobVar}} = new BlobBuilder();

                                   new BlobEncoder({{methodSignatureBlobVar}}).
                                       MethodSignature(isInstanceMethod: {{isInstanceMethod.ToKeyword()}}, genericParameterCount: {{method.TypeArguments.Length}}).
                                       Parameters({{method.Parameters.Length}},
                                           returnType => returnType.{{ctx.TypedTypeResolver.Resolve(method.OriginalDefinition.ReturnType, method.ToTypeResolutionContext())}},
                                           parameters =>
                                           {
                                               {{
                                                   string.Join('\n',
                                                       method.OriginalDefinition.Parameters.Select(p => $"""
                                                                                                             parameters.AddParameter().{ctx.TypedTypeResolver.Resolve(p.Type, new TypeResolutionContext(ResolveTargetKind.Parameter, TypeResolutionOptionsFor(p)))};
                                                                                                         """))}}
                                           });

                                   var {{methodSignatureVarName}} = metadata.GetOrAddBlob({{methodSignatureBlobVar}});
                                   """);
            });

            var containingTypeRefVar = context.TypeResolver.Resolve(method.ContainingType, ResolveTargetKind.TypeReference);
            context.Generate($$"""
                               var {{methodRefVar}} = metadata.AddMemberReference(
                                                                   {{containingTypeRefVar}},
                                                                   metadata.GetOrAddString("{{method.MappedName()}}"),
                                                                   {{methodSignatureVar.VariableName}});
                               """);

            context.WriteNewLine();
            context.DefinitionVariables.RegisterMethod(toBeFound.WithVariableName(methodRefVar));
        }

        if (method.IsGenericMethod)
        {
            var tbf = method.AsRawMethodDefinitionVariable(VariableMemberKind.MethodInstantiation);
            var instantiationVar = FindOrRegisterVariable(context, method, tbf, context.Naming.GenericInstance(method), methodRefVar, static (ctx, method, methodSpecificationVar, openMethodVar) =>
            {
                ctx.Generate($$"""
                                   MethodSpecificationHandle {{methodSpecificationVar}}; 
                                   {
                                       var tempMethodSignature = new BlobEncoder(new BlobBuilder()).MethodSpecificationSignature({{method.TypeArguments.Length}});
                                       {{
                                           string.Join('\n', method.TypeArguments.Select(typeArgument => $"tempMethodSignature.AddArgument().{ctx.TypeResolver.Resolve(typeArgument, ResolveTargetKind.GenericTypeArgument.ToTypeResolutionContext())}; // {typeArgument.Name}"))
                                       }}
                                       {{methodSpecificationVar}} = metadata.AddMethodSpecification({{openMethodVar}}, metadata.GetOrAddBlob(tempMethodSignature.Builder));
                                   }
                                   """);
                ctx.WriteNewLine();
            });
            
            methodRefVar = instantiationVar.VariableName;
        }
        return methodRefVar;
    }
    
    public string ResolveMethod(string declaringTypeName, string declaringTypeVariable, string methodName, ResolvedType returnType, IReadOnlyList<ParameterSpec> parameters, IReadOnlyList<string> typeParameters, MemberOptions options)
    {
        var methodReferenceToFind = new MethodDefinitionVariable(
                                                VariableMemberKind.MethodReference,
                                                declaringTypeName,
                                                methodNameForVariableRegistration,
                                                parameters.Select(p => p.ElementType.Expression).ToArray(),
                                                typeParameters.ToArray());

        var found = context.DefinitionVariables.GetMethodVariable(methodReferenceToFind);
        if (found.IsValid)
            return found.VariableName;

        var methodSignatureBlobVar = context.Naming.SyntheticVariable($"{methodNameForVariableRegistration}_blobBuilder", ElementKind.MemberReference);
        var methodSignatureVar = context.Naming.SyntheticVariable($"{methodNameForVariableRegistration}_Signature", ElementKind.MemberReference);
        var methodRefVar = context.Naming.SyntheticVariable($"{methodNameForVariableRegistration}", ElementKind.MemberReference);
        
        context.DefinitionVariables.RegisterMethod(new MethodDefinitionVariable(
                                                            VariableMemberKind.MethodSignature,
                                                            declaringTypeName,
                                                            methodNameForVariableRegistration,
                                                            parameters.Select(p => p.ElementType.Expression).ToArray(),
                                                            typeParameters.ToArray(),
                                                            methodSignatureVar));

        var requiredModifierOrEmpty = string.Empty;
        if ((options & MemberOptions.InitOnly) != 0)
        {
            var modifierVariable = context.TypedTypeResolver.Resolve(context.RoslynTypeSystem.ForType(typeof(IsExternalInit).FullName), ResolveTargetKind.TypeReference);
            requiredModifierOrEmpty = $"returnTypeEncoder.CustomModifiers().AddModifier({modifierVariable}, isOptional: false);";
        }

        context.Generate($$"""
                              var {{methodSignatureBlobVar}} = new BlobBuilder();

                              new BlobEncoder({{methodSignatureBlobVar}}).
                                  MethodSignature(isInstanceMethod: {{(options != MemberOptions.Static).ToKeyword()}}).
                                  Parameters({{parameters.Count}},
                                      returnTypeEncoder => 
                                      {
                                        {{requiredModifierOrEmpty}}
                                        returnTypeEncoder.{{returnType}};
                                      },
                                      parameters => 
                                      {
                                          {{
                                              string.Join('\n', parameters.Select(p => $"parameters.AddParameter().{ p.ElementTypeResolver?.Invoke(context, p) ?? p.ElementType.Expression };"))
                                          }}
                                      });

                              var {{methodSignatureVar}} = metadata.GetOrAddBlob({{methodSignatureBlobVar}});
                              var {{methodRefVar}} = metadata.AddMemberReference(
                                                                  {{declaringTypeVariable}},
                                                                  metadata.GetOrAddString("{{methodNameForVariableRegistration}}"),
                                                                  {{methodSignatureVar}});
                              """);

          context.WriteNewLine();
          context.DefinitionVariables.RegisterMethod(methodReferenceToFind.WithVariableName(methodRefVar));

          return methodRefVar;
    }

    public string ResolveDefaultConstructor(ITypeSymbol baseType, string _/*derivedTypeVar*/)
    {
        var parameterlessCtor = baseType.GetMembers(".ctor").OfType<IMethodSymbol>().Single(m => m.Parameters.Length == 0);
        return ResolveMethod(parameterlessCtor);
    }

    public string ResolveField(IFieldSymbol field)
    {
        var found = context.DefinitionVariables.GetVariable(field.Name, VariableMemberKind.Field, field.ContainingType.ToDisplayString());
        if (found.IsValid)
            return found.VariableName;

        var resolvedDeclaringType = context.TypeResolver.Resolve(field.ContainingType, new TypeResolutionContext(ResolveTargetKind.TypeReference, TypeResolutionOptions.None));

        var fieldSignatureVarName = context.Naming.SyntheticVariable($"{field.ToValidVariableName()}_Signature", ElementKind.MemberReference);
        var fieldRefVarName = context.Naming.SyntheticVariable(field.Name, ElementKind.Field);
        var typeResolver = (SystemReflectionMetadataTypeResolver) context.TypeResolver;
        context.Generate($"""
                          BlobBuilder {fieldSignatureVarName} = new();
                          new BlobEncoder({fieldSignatureVarName}).Field().{typeResolver.Resolve(field.Type, new TypeResolutionContext(ResolveTargetKind.Field, field.RefKind != RefKind.None ? TypeResolutionOptions.IsByRef : TypeResolutionOptions.None))};
                          var {fieldRefVarName} = metadata.AddMemberReference({resolvedDeclaringType}, metadata.GetOrAddString("{field.Name}"), metadata.GetOrAddBlob({fieldSignatureVarName}));
                          """);

        context.WriteNewLine();

        context.DefinitionVariables.RegisterNonMethod(field.ContainingType.ToDisplayString(), field.Name, VariableMemberKind.Field, fieldRefVarName);

        return fieldRefVarName;
    }

    public string ResolveEventField(IEventSymbol aEvent)
    {
        throw new NotImplementedException();
    }

    public string ImportReference(string expression) => expression; // In SRM this is a noop

    public string MakeGeneticInstanceMethod(string methodReferenceVariable, string methodName, IReadOnlyList<ResolvedType> resolvedTypeArguments)
    {
        // TODO: Pass method's parent name.
        var tbf = new MethodDefinitionVariable(VariableMemberKind.MethodInstantiation, "parent?", methodName, [], resolvedTypeArguments.Select(rt => rt.Expression).ToArray());
        var instantiationVar = FindOrRegisterVariable(context, resolvedTypeArguments, tbf, context.Naming.SyntheticVariable(methodName, ElementKind.GenericInstance), methodReferenceVariable, static (ctx, typeArguments, methodSpecificationVar, openMethodVar) =>
        {
            ctx.Generate($$"""
                           MethodSpecificationHandle {{methodSpecificationVar}}; 
                           {
                               var tempMethodSignature = new BlobEncoder(new BlobBuilder()).MethodSpecificationSignature({{typeArguments.Count}});
                               {{
                                   string.Join('\n', typeArguments.Select(typeArgument => $"tempMethodSignature.AddArgument().{typeArgument};"))
                               }}
                               {{methodSpecificationVar}} = metadata.AddMethodSpecification({{openMethodVar}}, metadata.GetOrAddBlob(tempMethodSignature.Builder));
                           }
                           """);
            ctx.WriteNewLine();
        });
            
        return  instantiationVar.VariableName;
    }

    #region Non public members
    private static DefinitionVariable FindOrRegisterVariable(SystemReflectionMetadataContext context, IReadOnlyList<ResolvedType> resolvedTypeArguments, MethodDefinitionVariable tbf, string variableNameToRegister, string openMethodVar, Action<SystemReflectionMetadataContext, IReadOnlyList<ResolvedType>, string, string> action)
    {
        var instantiationVar = context.DefinitionVariables.GetMethodVariable(tbf);
        if (!instantiationVar.IsValid)
        {
            action(context, resolvedTypeArguments, variableNameToRegister, openMethodVar);
            instantiationVar = context.DefinitionVariables.RegisterMethod(tbf.WithVariableName(variableNameToRegister));
        }

        return instantiationVar;
    }
    
    private static DefinitionVariable FindOrRegisterVariable(SystemReflectionMetadataContext context, IMethodSymbol method, MethodDefinitionVariable tbf, string variableNameToRegister, string openMethodVar, Action<SystemReflectionMetadataContext, IMethodSymbol, string, string> action)
    {
        var instantiationVar = context.DefinitionVariables.GetMethodVariable(tbf);
        if (!instantiationVar.IsValid)
        {
            action(context, method, variableNameToRegister, openMethodVar);
            instantiationVar = context.DefinitionVariables.RegisterMethod(tbf.WithVariableName(variableNameToRegister));
        }

        return instantiationVar;
    }
    
    private static TypeResolutionOptions TypeResolutionOptionsFor(IParameterSymbol parameter)
    {
        var byRefState= parameter.RefKind != RefKind.None 
            ? TypeResolutionOptions.IsByRef 
            : TypeResolutionOptions.None;
            
        return (parameter.Type.IsValueType ? TypeResolutionOptions.IsValueType : TypeResolutionOptions.None) | byRefState;
    }
    #endregion
}
