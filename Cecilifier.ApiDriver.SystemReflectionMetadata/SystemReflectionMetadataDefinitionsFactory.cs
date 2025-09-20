using System.Diagnostics;
using Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;
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
                                                                    fieldList: {typeRecord.FirstFieldHandle ?? "MetadataTokens.FieldDefinitionHandle(1)"},
                                                                    methodList: {typeRecord.FirstMethodHandle ?? "MetadataTokens.MethodDefinitionHandle(1)"});
                                                       """));

        });
    }

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, MemberDefinitionContext memberDefinitionContext, string methodName, string methodModifiers, IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters)
    {
        // Resolve the method to make sure there's a method ref available (this will be used to fulfill any references to this method)
        context.MemberResolver.ResolveMethod(methodSymbol);

        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterMethodDefinition(memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            var methodSignatureVar = ctx.DefinitionVariables.GetVariable(methodSymbol.Name, VariableMemberKind.MethodSignature, methodSymbol.ContainingSymbol.ToDisplayString());
            Debug.Assert(methodSignatureVar.IsValid);
            
            var methodDefVar = context.Naming.SyntheticVariable(methodName, ElementKind.Method);
            ctx.Generate($"""
                          var {methodDefVar}  = metadata.AddMethodDefinition(
                                                    {methodModifiers},
                                                    MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                    metadata.GetOrAddString("{methodName}"),
                                                    {methodSignatureVar.VariableName},
                                                    methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}),
                                                    parameterList: {methodRecord.FirstParameterHandle});
                          """);
            
            ctx.WriteNewLine();
            
            ctx.DefinitionVariables.RegisterMethod(methodSymbol.AsMethodDefinitionVariable(methodDefVar));
            
            return methodDefVar;
        });
        
        yield break;
    }

    public IEnumerable<string> Method(
        IVisitorContext context,
        MemberDefinitionContext memberDefinitionContext,
        string declaringTypeName, 
        string methodNameForVariableRegistration, 
        string methodName, 
        string methodModifiers, 
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters, 
        ITypeSymbol returnType, 
        out MethodDefinitionVariable methodDefinitionVariable)
    {
        var typedTypeResolver = (SystemReflectionMetadataTypeResolver) context.TypeResolver;
        context.MemberResolver.ResolveMethod(
            declaringTypeName, 
            memberDefinitionContext.ParentDefinitionVariableName, 
            methodNameForVariableRegistration, 
            typedTypeResolver.ResolveForEncoder(returnType, TargetEncoderKind.ReturnType, false), 
            parameters, 
            typeParameters.Count);
        
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterMethodDefinition(memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            var methodReferenceToFind = new MethodDefinitionVariable(
                                                VariableMemberKind.MethodSignature,
                                                declaringTypeName,
                                                methodNameForVariableRegistration,
                                                parameters.Select(p => p.ElementType).ToArray(),
                                                typeParameters.Count);

            var methodSignatureVar = ctx.DefinitionVariables.GetMethodVariable(methodReferenceToFind);
            Debug.Assert(methodSignatureVar.IsValid);
            
            var methodDefVar = memberDefinitionContext.MemberDefinitionVariableName;
            ctx.Generate($"""
                          var {methodDefVar}  = metadata.AddMethodDefinition(
                                                    {methodModifiers},
                                                    MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                    metadata.GetOrAddString("{methodName}"),
                                                    {methodSignatureVar.VariableName},
                                                    methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}),
                                                    parameterList: {methodRecord.FirstParameterHandle});
                          """);
            
            ctx.WriteNewLine();
        
            var toBeRegistered = new MethodDefinitionVariable(
                                        VariableMemberKind.Method,
                                        declaringTypeName,
                                        methodName,
                                        parameters.Select(p => p.ElementType).ToArray(),
                                        0);
            
            ctx.DefinitionVariables.RegisterMethod(toBeRegistered);
            
            return methodDefVar;
        });
        
        //TODO: Can re remove methodDefinitionVariable ?
        methodDefinitionVariable = new MethodDefinitionVariable(VariableMemberKind.Method, string.Empty, string.Empty, null, 0);
        
        return [];
    }

    public IEnumerable<string> Constructor(IVisitorContext context, MemberDefinitionContext memberDefinitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null)
    {
        var parameterlessCtorSignatureVar = context.Naming.SyntheticVariable("parameterlessCtorSignature", ElementKind.LocalVariable);
        yield return Format(
            $$"""
              var {{parameterlessCtorSignatureVar}} = new BlobBuilder();
              new BlobEncoder({{parameterlessCtorSignatureVar}})
                     .MethodSignature(isInstanceMethod: {{ (!isStatic).ToKeyword()}})
                     .Parameters(0, returnType => returnType.Void(), parameters => { });
              """);
        
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterMethodDefinition(memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            var ctorDefVar = ctx.Naming.SyntheticVariable("ctor", ElementKind.LocalVariable);
            ctx.Generate($"""
                                   var {ctorDefVar} = metadata.AddMethodDefinition(
                                                             {(isStatic ? "MethodAttributes.Private | MethodAttributes.Static" : "MethodAttributes.Public")} | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                                             MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                             metadata.GetOrAddString("{(isStatic ? ".cctor" : ".ctor")}"),
                                                             metadata.GetOrAddBlob({parameterlessCtorSignatureVar}),
                                                             methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}),
                                                             parameterList: {methodRecord.FirstParameterHandle});
                                   """);
            
            ctx.WriteNewLine();
            return ctorDefVar;
        });
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        //TODO: Handle isByRef
        Debug.Assert(isByRef == false, "Handle isByRef");
        var typedTypeResolver = (SystemReflectionMetadataTypeResolver) context.TypeResolver;
        
        context.DefinitionVariables.RegisterNonMethod(fieldOrEvent.ContainingType.ToDisplayString(), fieldOrEvent.Name, VariableMemberKind.Field, memberDefinitionContext.MemberDefinitionVariableName);
        var fieldSignatureVar = context.Naming.SyntheticVariable($"{fieldOrEvent.Name}_fs", ElementKind.LocalVariable);
        return [
            $"""
             BlobBuilder {fieldSignatureVar} = new();
             new BlobEncoder({fieldSignatureVar})
                 .FieldSignature()
                 .{typedTypeResolver.ResolveForEncoder(fieldType, TargetEncoderKind.Field, false)};
                 
             var {memberDefinitionContext.MemberDefinitionVariableName} = metadata.AddFieldDefinition({fieldAttributes}, metadata.GetOrAddString("{fieldOrEvent.Name}"), metadata.GetOrAddBlob({fieldSignatureVar}));
             """
        ];
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, string declaringTypeName, string name, string fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        context.DefinitionVariables.RegisterNonMethod(declaringTypeName, name, VariableMemberKind.Field, memberDefinitionContext.MemberDefinitionVariableName);
        var fieldSignatureVar = context.Naming.SyntheticVariable($"{name}_fs", ElementKind.LocalVariable);
        return [
            $"""
             BlobBuilder {fieldSignatureVar} = new();
             new BlobEncoder({fieldSignatureVar})
                 .FieldSignature()
                 TODO: Handle Field Type Resolution.;
                 
             var {memberDefinitionContext.MemberDefinitionVariableName} = metadata.AddFieldDefinition({fieldAttributes}, metadata.GetOrAddString("{name}"), metadata.GetOrAddBlob({fieldSignatureVar}));
             """
        ];
    }

    public IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, string[] localVariableTypes, InstructionRepresentation[] instructions) => [];

    static string Format(CecilifierInterpolatedStringHandler cecilFormattedString) => cecilFormattedString.Result;
}
