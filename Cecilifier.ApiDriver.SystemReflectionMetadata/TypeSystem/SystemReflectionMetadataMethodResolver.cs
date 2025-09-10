using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataMethodResolver(SystemReflectionMetadataContext context) : IMethodResolver
{
    public string Resolve(IMethodSymbol method)
    {
        var containingTypeRefVar= context.TypeResolver.ResolveAny(method.ContainingType);
        var methodSignatureBlobVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}Signature", ElementKind.LocalVariable);
        var methodRefVar = context.Naming.SyntheticVariable($"{method.ToValidVariableName()}Ref", ElementKind.LocalVariable);
        
        context.Generate($"""
                            var {methodSignatureBlobVar} = new BlobBuilder();

                            new BlobEncoder({methodSignatureBlobVar}).
                                MethodSignature().
                                Parameters(1,
                                    returnType => returnType.Void(),
                                    parameters => parameters.AddParameter().Type().String());

                            var {methodRefVar} = metadata.AddMemberReference(
                                                                {containingTypeRefVar},
                                                                metadata.GetOrAddString("{method.Name}"),
                                                                metadata.GetOrAddBlob({methodSignatureBlobVar}));
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
}
