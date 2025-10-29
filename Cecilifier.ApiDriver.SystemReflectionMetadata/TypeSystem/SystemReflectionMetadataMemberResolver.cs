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
        var toBeFound = method.AsMethodVariable(VariableMemberKind.MethodReference);
        var found = context.DefinitionVariables.GetMethodVariable(toBeFound);
        if (found.IsValid)
            return found.VariableName;
            
        var containingTypeRefVar= context.TypeResolver.ResolveAny(method.ContainingType);
        var methodSignatureBlobVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}BlobBuilder", ElementKind.MemberReference);
        var methodRefVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}", ElementKind.MemberReference);
        
        var methodSignatureVar = context.Naming.SyntheticVariable($"{method.Name}Signature", ElementKind.MemberReference);
        context.DefinitionVariables.RegisterMethod(method.AsMethodVariable(VariableMemberKind.MethodSignature, methodSignatureVar));

        var isInstanceMethod = !method.IsStatic && method.MethodKind != MethodKind.LocalFunction; // local functions are always declared as static (we don't support capturing variables)
        context.Generate($$"""
                           var {{methodSignatureBlobVar}} = new BlobBuilder();

                           new BlobEncoder({{methodSignatureBlobVar}}).
                               MethodSignature(isInstanceMethod: {{ isInstanceMethod.ToKeyword() }}).
                               Parameters({{method.Parameters.Length}},
                                   returnType => returnType.{{context.TypedTypeResolver.ResolveForTargetKind(method.ReturnType, ResolveTargetKind.ReturnType, method.IsByRef())}},
                                   parameters => 
                                   {
                                       {{
                                           string.Join('\n',
                                               method.Parameters.Select(p => $"""
                                                                                  parameters
                                                                                          .AddParameter()
                                                                                          .{context.TypedTypeResolver.ResolveForTargetKind(p.Type, ResolveTargetKind.Parameter, p.RefKind != RefKind.None)};
                                                                              """))}}
                                   });

                           var {{methodSignatureVar}} = metadata.GetOrAddBlob({{methodSignatureBlobVar}});
                           var {{methodRefVar}} = metadata.AddMemberReference(
                                                               {{containingTypeRefVar}},
                                                               metadata.GetOrAddString("{{method.MappedName()}}"),
                                                               {{methodSignatureVar}});
                           """);
        
        context.WriteNewLine();
        
        context.DefinitionVariables.RegisterMethod(toBeFound.WithVariableName(methodRefVar));
        return methodRefVar;
    }
    
    public string ResolveMethod(string declaringTypeName, string declaringTypeVariable, string methodNameForVariableRegistration, string resolvedReturnType, IReadOnlyList<ParameterSpec> parameters, int typeParameterCountCount, MemberOptions options)
    {
        var methodReferenceToFind = new MethodDefinitionVariable(
                                                VariableMemberKind.MethodReference,
                                                declaringTypeName,
                                                methodNameForVariableRegistration,
                                                parameters.Select(p => p.ElementType).ToArray(),
                                                typeParameterCountCount);
        
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
                                                            parameters.Select(p => p.ElementType).ToArray(),
                                                            typeParameterCountCount,
                                                            methodSignatureVar));

        var modifierVariable = string.Empty;
        if ((options & MemberOptions.InitOnly) != 0)
        {
            modifierVariable = context.TypedTypeResolver.Resolve(context.RoslynTypeSystem.ForType(typeof(IsExternalInit).FullName));
        }
        var requiredModifierOrEmpty = (options & MemberOptions.InitOnly) != 0 
                                    ? $"returnTypeEncoder.CustomModifiers().AddModifier({modifierVariable}, isOptional: false);" 
                                    : "";
        
        context.Generate($$"""
                              var {{methodSignatureBlobVar}} = new BlobBuilder();

                              new BlobEncoder({{methodSignatureBlobVar}}).
                                  MethodSignature(isInstanceMethod: {{(options != MemberOptions.Static).ToKeyword()}}).
                                  Parameters({{parameters.Count}},
                                      returnTypeEncoder => 
                                      {
                                        {{requiredModifierOrEmpty}}
                                        returnTypeEncoder.{{resolvedReturnType}};
                                      },
                                      parameters => 
                                      {
                                          {{
                                              string.Join('\n', parameters.Select(p => $"parameters.AddParameter().{p.ElementType};"))
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

    public string ResolveDefaultConstructor(ITypeSymbol baseType, string derivedTypeVar)
    {
        var voidParameterlessMethodRef = context.DefinitionVariables.GetVariable("voidParameterlessMethodRef", VariableMemberKind.MethodReference);
        if (!voidParameterlessMethodRef.IsValid)
        {
            var voidParameterlessMethodRefVarName = context.Naming.SyntheticVariable("voidParameterlessMethod", ElementKind.MemberReference);
            context.Generate($$"""
                                          var parameterlessCtorSignature = new BlobBuilder();
                                          
                                          new BlobEncoder(parameterlessCtorSignature).
                                                 MethodSignature(isInstanceMethod: true).
                                                 Parameters(0, returnType => returnType.Void(), parameters => { });
                                          
                                          var parameterlessCtorBlobIndex = metadata.GetOrAddBlob(parameterlessCtorSignature);
                                          
                                          var {{voidParameterlessMethodRefVarName}} = metadata.AddMemberReference(
                                                                                                    {{context.TypeResolver.ResolveAny(baseType)}},
                                                                                                    metadata.GetOrAddString(".ctor"),
                                                                                                    parameterlessCtorBlobIndex);
                                          """);
            
            voidParameterlessMethodRef = context.DefinitionVariables.RegisterNonMethod("", "voidParameterlessMethodRef", VariableMemberKind.MethodReference, voidParameterlessMethodRefVarName);
        }
        
        return voidParameterlessMethodRef.VariableName;
    }
    
    public string ResolveField(IFieldSymbol field)
    {
        var found = context.DefinitionVariables.GetVariable(field.Name, VariableMemberKind.Field, field.ContainingType.ToDisplayString());
        if (found.IsValid)
            return found.VariableName;

        var resolvedDeclaringType = context.TypeResolver.ResolveAny(field.ContainingType);

        var fieldSignatureVarName = context.Naming.SyntheticVariable($"{field.ToValidVariableName()}_Signature", ElementKind.MemberReference);
        var fieldRefVarName = context.Naming.SyntheticVariable(field.Name, ElementKind.Field);
        var typeResolver = (SystemReflectionMetadataTypeResolver) context.TypeResolver;
        context.Generate($"""
                          BlobBuilder {fieldSignatureVarName} = new();
                          new BlobEncoder({fieldSignatureVarName}).FieldSignature().{typeResolver.ResolveForTargetKind(field.Type, ResolveTargetKind.Field, false)};
                          var {fieldRefVarName} = metadata.AddMemberReference({resolvedDeclaringType}, metadata.GetOrAddString("{field.Name}"), metadata.GetOrAddBlob({fieldSignatureVarName}));
                          """);
        
        context.WriteNewLine();
        
        context.DefinitionVariables.RegisterNonMethod(field.ContainingType.ToDisplayString(), field.Name, VariableMemberKind.Field, fieldRefVarName);
        
        return fieldRefVarName;
    }
}
