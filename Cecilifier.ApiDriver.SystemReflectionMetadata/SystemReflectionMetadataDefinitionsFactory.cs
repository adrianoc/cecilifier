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
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers)
    {
        return RoslynToApiDriverModifiers(type, modifiers); 
        //return "TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.AutoLayout | TypeAttributes.BeforeFieldInit";
    }
    
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
        
        yield return Format($"""
                      metadata.AddTypeDefinition(
                                     {attrs}, // TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.AutoLayout | TypeAttributes.BeforeFieldInit
                                     metadata.GetOrAddString("{typeNamespace}"),
                                     metadata.GetOrAddString("{typeName}"),
                                     {resolvedBaseType},
                                     fieldList: MetadataTokens.FieldDefinitionHandle(1),
                                     methodList: MetadataTokens.MethodDefinitionHandle(1));
                      """);
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

        var expressions = new List<CecilifierInterpolatedStringHandler>
        {
            $$"""
             var {{parameterlessCtorSignatureVar}} = new BlobBuilder();
             new BlobEncoder({{parameterlessCtorSignatureVar}})
                    .MethodSignature(isInstanceMethod: {{(isStatic ? "false" : "true")}})
                    .Parameters(0, returnType => returnType.Void(), parameters => { });
             
             var {{ctorBlobIndexVar}} = metadata.GetOrAddBlob({{parameterlessCtorSignatureVar}});
                
             var objectCtorMemberRef = metadata.AddMemberReference(
                                                 {{context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemObject)}},
                                                 metadata.GetOrAddString(".ctor"),
                                                 {{ctorBlobIndexVar}});
             """
        };

        return expressions.Select(cish => cish.Result);

    }

    static string Format(CecilifierInterpolatedStringHandler handler) => handler.Result;
}
