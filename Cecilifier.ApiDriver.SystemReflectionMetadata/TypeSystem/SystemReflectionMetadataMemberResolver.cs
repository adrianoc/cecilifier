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
        context.DefinitionVariables.RegisterNonMethod(method.ContainingSymbol.ToDisplayString(), method.Name, VariableMemberKind.MethodSignature, methodSignatureVar);
        
        context.Generate($$"""
                            var {{methodSignatureBlobVar}} = new BlobBuilder();

                            new BlobEncoder({{methodSignatureBlobVar}}).
                                MethodSignature(isInstanceMethod: {{ (!method.IsStatic).ToKeyword() }}).
                                Parameters({{method.Parameters.Length}},
                                    returnType => returnType.{{context.TypedTypeResolver.ResolveForEncoder(method.ReturnType, TargetEncoderKind.ReturnType, method.IsByRef())}},
                                    parameters => 
                                    {
                                        {{
                                            string.Join('\n',
                                                method.Parameters.Select(p => $"""
                                                                               parameters
                                                                                       .AddParameter()
                                                                                       .{context.TypedTypeResolver.ResolveForEncoder(p.Type, TargetEncoderKind.Parameter, p.RefKind != RefKind.None)};
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

    public string ResolveDefaultConstructor(ITypeSymbol baseType, string derivedTypeVar)
    {
        var voidParameterlessMethodRef = context.DefinitionVariables.GetVariable("voidParameterlessMethodRef", VariableMemberKind.LocalVariable);
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
            
            voidParameterlessMethodRef = context.DefinitionVariables.RegisterNonMethod("", "voidParameterlessMethodRef", VariableMemberKind.LocalVariable, voidParameterlessMethodRefVarName);
        }
        
        return voidParameterlessMethodRef.VariableName;
    }
    
    public string ResolveField(IFieldSymbol field)
    {
        var found = context.DefinitionVariables.GetVariable(field.Name, VariableMemberKind.Field, field.ContainingType.ToDisplayString());
        if (found.IsValid)
            return found.VariableName;
        
        var declaringTypeVar = context.DefinitionVariables.GetVariable(field.ContainingType.ToDisplayString(), VariableMemberKind.Type, field.ContainingType.ContainingType?.ToDisplayString());
        declaringTypeVar.ThrowIfVariableIsNotValid();

        var fieldSignatureVarName = context.Naming.SyntheticVariable($"{field.ToValidVariableName()}Signature", ElementKind.LocalVariable);
        var fieldRefVarName = context.Naming.SyntheticVariable(field.Name, ElementKind.Field);
        var typeResolver = (SystemReflectionMetadataTypeResolver) context.TypeResolver;
        context.Generate($"""
                          BlobBuilder {fieldSignatureVarName} = new();
                          new BlobEncoder({fieldSignatureVarName}).FieldSignature().{typeResolver.ResolveForEncoder(field.Type, TargetEncoderKind.Field, false)};
                          var {fieldRefVarName} = metadata.AddMemberReference({declaringTypeVar.VariableName}, metadata.GetOrAddString("{field.Name}"), metadata.GetOrAddBlob({fieldSignatureVarName}));
                          """);
        
        context.WriteNewLine();
        
        context.DefinitionVariables.RegisterNonMethod(field.ContainingType.ToDisplayString(), field.Name, VariableMemberKind.Field, fieldRefVarName);
        
        return fieldRefVarName;
    }
}
