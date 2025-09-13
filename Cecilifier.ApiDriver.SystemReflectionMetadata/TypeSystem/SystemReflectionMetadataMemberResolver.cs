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
        var containingTypeRefVar= context.TypeResolver.ResolveAny(method.ContainingType);
        var methodSignatureBlobVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}Signature", ElementKind.LocalVariable);
        var methodRefVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}Ref", ElementKind.LocalVariable);
        
        context.Generate($$"""
                            var {{methodSignatureBlobVar}} = new BlobBuilder();

                            new BlobEncoder({{methodSignatureBlobVar}}).
                                MethodSignature().
                                Parameters(1,
                                    returnType => returnType.{{context.TypeTypeResolver.ResolveForEncoder(method.ReturnType, TargetEncoderKind.ReturnType, method.IsByRef())}},
                                    parameters => 
                                    {
                                        {{
                                            string.Join('\n',
                                                method.Parameters.Select(p => $"""
                                                                               parameters
                                                                                       .AddParameter()
                                                                                       .{context.TypeTypeResolver.ResolveForEncoder(p.Type, TargetEncoderKind.Parameter, p.RefKind != RefKind.None)};
                                                                           """))}}
                                    });

                            var {{methodRefVar}} = metadata.AddMemberReference(
                                                                {{containingTypeRefVar}},
                                                                metadata.GetOrAddString("{{method.Name}}"),
                                                                metadata.GetOrAddBlob({{methodSignatureBlobVar}}));
                            """);
        
        context.WriteNewLine();
        return methodRefVar;
    }

    public string ResolveDefaultConstructor(ITypeSymbol type, string derivedTypeVar)
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
                                                                                                    {{context.TypeResolver.ResolveAny(type)}},
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
