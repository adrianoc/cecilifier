using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
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
        yield return Format($"""
                      // Add a type reference for the new type. Types/Member references to the new type uses this.
                      var {typeVar} = metadata.AddTypeReference(
                                                  mainModuleHandle, 
                                                  metadata.GetOrAddString("{typeNamespace}"), 
                                                  metadata.GetOrAddString("{typeName}"));
                      
                      """);

        // We need to pass the handle of the 1st field/method defined in the module so we need to postpone the type generation after we have visited
        // all types/members.
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterTypeDefinition(typeVar, $"{typeNamespace}.{typeName}", (ctx, typeRecord) =>
        {
            ctx.Generate(Format($"""
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

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, MemberDefinitionContext memberDefinitionContext, string methodName, string methodModifiers, IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters)
    {
        var methodSignatureVar = context.Naming.SyntheticVariable($"{methodName}Signature", ElementKind.LocalVariable);
        var methodBlobIndexVar = context.Naming.SyntheticVariable($"{methodName}BlobIndex", ElementKind.LocalVariable);

        yield return Format(
            $$"""
              var {{methodSignatureVar}} = new BlobBuilder();
              new BlobEncoder({{methodSignatureVar}})
                     .MethodSignature(isInstanceMethod: false)
                     .Parameters(
                            {{resolvedParameterTypes.Length}}, 
                            returnType => returnType
                                                .Type(isByRef:false)
                                                .Type({{context.TypeResolver.ResolveAny(methodSymbol.ReturnType)}}, IsValueType: {{methodSymbol.ReturnType.IsValueType}}), 
                            parameters => 
                            {
                                {{string.Join('\n', resolvedParameterTypes.Select(p => $"""
                                                                         parameters.AddParameter()
                                                                                .Type(isByRef: {p.IsByRef()})
                                                                                .Type({context.TypeResolver.ResolveAny(p.Type)}, isValueType: {p.Type.IsValueType.ToKeyword()});
                                                                      """))}}
                            });

              var {{methodBlobIndexVar}} = metadata.GetOrAddBlob({{methodSignatureVar}});
                 
              var {{context.Naming.SyntheticVariable($"{methodName}Ref", ElementKind.LocalVariable)}} = metadata.AddMemberReference(
                                                  {{memberDefinitionContext.ParentDefinitionVariableName}},
                                                  metadata.GetOrAddString("{{methodName}}"),
                                                  {{methodBlobIndexVar}});
              """);
        
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterMethodDefinition(memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            var methodDefVar = ctx.Naming.SyntheticVariable(methodName, ElementKind.LocalVariable);
            ctx.Generate($"""
                                   var {methodDefVar} = metadata.AddMethodDefinition(
                                                             {methodModifiers},
                                                             MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                             metadata.GetOrAddString("{methodName}"),
                                                             metadata.GetOrAddBlob({methodSignatureVar}),
                                                             methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}),
                                                             parameterList: {methodRecord.FirstParameterHandle});
                                   """);
            
            ctx.WriteNewLine();
            return methodDefVar;
        });
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
            ctx.Generate($"""
                                   var {ctorDefVar} = metadata.AddMethodDefinition(
                                                             MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                                             MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                             metadata.GetOrAddString(".ctor"),
                                                             metadata.GetOrAddBlob({parameterlessCtorSignatureVar}),
                                                             methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}),
                                                             parameterList: {methodRecord.FirstParameterHandle});
                                   """);
            
            ctx.WriteNewLine();
            return ctorDefVar;
        });
    }

    static string Format(CecilifierInterpolatedStringHandler cecilFormattedString) => cecilFormattedString.Result;
}
