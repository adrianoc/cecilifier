using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public class SystemReflectionMetadataMethodResolver(SystemReflectionMetadataContext context) : IMethodResolver
{
    private readonly SystemReflectionMetadataContext _context = context;

    public string Resolve(IMethodSymbol method)
    {
        return method.Name + " !! FIX ME !!";
    }

    public string ResolveDefaultConstructor(ITypeSymbol type, string derivedTypeVar)
    {
        /*
         *
        var parameterlessCtorSignature = new BlobBuilder();

           new BlobEncoder(parameterlessCtorSignature).
               MethodSignature(isInstanceMethod: true).
               Parameters(0, returnType => returnType.Void(), parameters => { });

           var parameterlessCtorBlobIndex = metadata.GetOrAddBlob(parameterlessCtorSignature);

           var objectCtorMemberRef = metadata.AddMemberReference(
               systemObjectTypeRef,
               metadata.GetOrAddString(".ctor"),
               parameterlessCtorBlobIndex);


         */

        var voidParameterlessMethodRef = _context.DefinitionVariables.GetVariable("voidParameterlessMethodRef", VariableMemberKind.LocalVariable);
        if (!voidParameterlessMethodRef.IsValid)
        {
            var voidParameterlessMethodRefVarName = _context.Naming.SyntheticVariable("voidParameterlessMethodRef", ElementKind.LocalVariable);
            _context.WriteCecilExpression($$"""
                                          var parameterlessCtorSignature = new BlobBuilder();
                                          
                                          new BlobEncoder(parameterlessCtorSignature).
                                                 MethodSignature(isInstanceMethod: true).
                                                 Parameters(0, returnType => returnType.Void(), parameters => { });
                                          
                                          var parameterlessCtorBlobIndex = metadata.GetOrAddBlob(parameterlessCtorSignature);
                                          
                                          var {{voidParameterlessMethodRefVarName}} = metadata.AddMemberReference(
                                                                                                    {{_context.TypeResolver.Resolve(type)}}
                                                                                                    metadata.GetOrAddString(".ctor"),
                                                                                                    parameterlessCtorBlobIndex);
                                          """);
            
            voidParameterlessMethodRef = _context.DefinitionVariables.RegisterNonMethod("", "voidParameterlessMethodRef", VariableMemberKind.LocalVariable, voidParameterlessMethodRefVarName);
        }
        
        return voidParameterlessMethodRef.VariableName;
    }
}
