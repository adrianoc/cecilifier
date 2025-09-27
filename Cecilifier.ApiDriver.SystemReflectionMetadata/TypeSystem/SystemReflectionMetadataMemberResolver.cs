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
        var methodSignatureBlobVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}Signature", ElementKind.LocalVariable);
        var methodRefVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}Ref", ElementKind.LocalVariable);
        
        var methodSignatureVar = context.Naming.SyntheticVariable($"{method.Name}Signature", ElementKind.LocalVariable);
        context.DefinitionVariables.RegisterMethod(method.AsMethodVariable(VariableMemberKind.MethodSignature, methodSignatureVar));

        context.Generate($$"""
                            var {{methodSignatureBlobVar}} = new BlobBuilder();

                            new BlobEncoder({{methodSignatureBlobVar}}).
                                MethodSignature(isInstanceMethod: {{ (!method.IsStatic).ToKeyword() }}).
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
                                                                metadata.GetOrAddString("{{method.Name}}"),
                                                                {{methodSignatureVar}});
                            """);
        
        context.WriteNewLine();
        
        context.DefinitionVariables.RegisterMethod(toBeFound.WithVariableName(methodRefVar));
        return methodRefVar;
    }
    
    public string ResolveMethod(string declaringTypeName, string declaringTypeVariable, string methodNameForVariableRegistration, string resolvedReturnType, IReadOnlyList<ParameterSpec> parameters, int typeParameterCountCount)
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
          
        var methodSignatureBlobVar = context.Naming.SyntheticVariable($"{methodNameForVariableRegistration}Signature", ElementKind.LocalVariable);
        var methodSignatureVar = context.Naming.SyntheticVariable("methodSignature", ElementKind.LocalVariable);
        var methodRefVar = context.Naming.SyntheticVariable($"{methodNameForVariableRegistration}Ref", ElementKind.LocalVariable);
        
        context.DefinitionVariables.RegisterMethod(new MethodDefinitionVariable(
                                                            VariableMemberKind.MethodSignature,
                                                            declaringTypeName,
                                                            methodNameForVariableRegistration,
                                                            parameters.Select(p => p.ElementType).ToArray(),
                                                            typeParameterCountCount,
                                                            methodSignatureVar));
          
        context.Generate($$"""
                              var {{methodSignatureBlobVar}} = new BlobBuilder();

                              new BlobEncoder({{methodSignatureBlobVar}}).
                                  MethodSignature(isInstanceMethod: false). //TODO: This is wrong. The value for this parameter needs to come from IApiDriverDefinitionsFactory.Method()
                                  Parameters({{parameters.Count}},
                                      returnType => returnType.{{resolvedReturnType}},
                                      parameters => 
                                      {
                                          {{
                                              string.Join('\n',
                                                  parameters.Select(p => $"""
                                                                                 parameters
                                                                                         .AddParameter()
                                                                                         .{p.ElementType};
                                                                             """))}}
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
            var voidParameterlessMethodRefVarName = context.Naming.SyntheticVariable("voidParameterlessMethodRef", ElementKind.LocalVariable);
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

        var fieldSignatureVarName = context.Naming.SyntheticVariable($"{field.ToValidVariableName()}Signature", ElementKind.LocalVariable);
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
