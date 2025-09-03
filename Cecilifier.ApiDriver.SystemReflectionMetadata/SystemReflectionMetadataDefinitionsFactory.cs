using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

internal class SystemReflectionMetadataDefinitionsFactory : DefinitionsFactoryBase, IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers) => RoslynToApiDriverModifiers(type, modifiers);

    public IEnumerable<string> Type(
        IVisitorContext context, 
        string typeVar, 
        string typeNamespace, 
        string typeName, 
        string attrs, 
        string resolvedBaseType, 
        DefinitionVariable outerTypeVariable, 
        bool isStructWithNoFields,
        IEnumerable<ITypeSymbol> interfaces, 
        IEnumerable<TypeParameterSyntax>? ownTypeParameters, 
        IEnumerable<TypeParameterSyntax> outerTypeParameters, 
        params string[] properties)
    {
        //TODO: We need to pass the handle of the 1st field/method defined in the module so we need to postpone the type generation after we have visited
        //      all types/members.
        yield return Format($"""
                      // Add a type reference for the new type. Types/Member references to the new type uses this.
                      var {typeVar} = metadata.AddTypeReference(
                                                  mainModuleHandle, 
                                                  metadata.GetOrAddString("{typeNamespace}"), 
                                                  metadata.GetOrAddString("{typeName}"));
                      
                      """);

        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterTypeDefinition(typeVar, $"{typeNamespace}.{typeName}", (ctx, typeRecord) =>
        {
            ctx.WriteCecilExpression(Format($"""
                                                       metadata.AddTypeDefinition(
                                                                    {attrs},
                                                                    metadata.GetOrAddString("{typeNamespace}"),
                                                                    metadata.GetOrAddString("{typeName}"),
                                                                    {resolvedBaseType},
                                                                    fieldList: {typeRecord.FirstFieldHandle},
                                                                    methodList: {typeRecord.FirstMethodHandle});
                                                       """));

        });
    }

    public IEnumerable<string> Method(IVisitorContext context, string methodVar, string methodName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> Method(IVisitorContext context, string declaringTypeName, string methodVar, string methodNameForParameterVariableRegistration, string methodName, string methodModifiers, IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters, Func<IVisitorContext, string> returnTypeResolver, out MethodDefinitionVariable methodDefinitionVariable)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> Constructor(IVisitorContext context, MemberDefinitionContext memberDefinitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null)
    {
        var parameterlessCtorSignatureVar = context.Naming.SyntheticVariable("parameterlessCtorSignature", ElementKind.LocalVariable);
        var ctorBlobIndexVar = context.Naming.SyntheticVariable("parameterlessCtorBlobIndex", ElementKind.LocalVariable);

        yield return Format(
            $$"""
              var {{parameterlessCtorSignatureVar}} = new BlobBuilder();
              new BlobEncoder({{parameterlessCtorSignatureVar}})
                     .MethodSignature(isInstanceMethod: {{(isStatic ? "false" : "true")}})
                     .Parameters(0, returnType => returnType.Void(), parameters => { });

              var {{ctorBlobIndexVar}} = metadata.GetOrAddBlob({{parameterlessCtorSignatureVar}});
                 
              var objectCtorMemberRef = metadata.AddMemberReference(
                                                  {{memberDefinitionContext.ParentDefinitionVariableName}},
                                                  metadata.GetOrAddString(".ctor"),
                                                  {{ctorBlobIndexVar}});
              """);
        
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterMethodDefinition( memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            var ctorDefVar = ctx.Naming.SyntheticVariable("ctor", ElementKind.LocalVariable);
            ctx.WriteCecilExpression($"""
                                   var {ctorDefVar} = metadata.AddMethodDefinition(
                                                             MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                                             MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                             metadata.GetOrAddString(".ctor"),
                                                             metadata.GetOrAddBlob({parameterlessCtorSignatureVar}),
                                                             methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}),
                                                             parameterList: {methodRecord.FirstParameterHandle});
                                   """);
            return ctorDefVar;
        });
    }

    static string Format(CecilifierInterpolatedStringHandler handler) => handler.Result;
}
