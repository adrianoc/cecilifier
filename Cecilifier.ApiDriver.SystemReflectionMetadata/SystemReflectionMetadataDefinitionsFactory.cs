using System.Diagnostics;
using Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;
using Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
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
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterTypeDefinition(typeVar, $"{typeNamespace}.{typeName}", DefineDelayed);
        void DefineDelayed(SystemReflectionMetadataContext ctx, TypeDefinitionRecord typeRecord)
        {
            var typeDefVar = ctx.Naming.Type(typeName, ElementKind.Class);
            ctx.Generate(Format($"""
                                 var {typeDefVar} = metadata.AddTypeDefinition(
                                                                  {attrs},
                                                                  metadata.GetOrAddString("{typeNamespace}"),
                                                                  metadata.GetOrAddString("{typeName}"),
                                                                  {resolvedBaseType},
                                                                  fieldList: {typeRecord.FirstFieldHandle ?? "MetadataTokens.FieldDefinitionHandle(1)"},
                                                                  methodList: {typeRecord.FirstMethodHandle ?? "MetadataTokens.MethodDefinitionHandle(1)"});
                                 """));
            
            foreach (var property in typeRecord.Properties)
            {
                // process each property passing the type definition variable (as opposed to the type reference variable) 
                property.Processor(context, property.Name, property.DefinitionVariable, property.DeclaringTypeName, typeDefVar);
            }
        }
    }

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, MemberDefinitionContext memberDefinitionContext, string methodName, string methodModifiers, IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters)
    {
        // Resolve the method to make sure there's a method ref available (this will be used to fulfill any references to this method)
        context.MemberResolver.ResolveMethod(methodSymbol);

        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterMethodDefinition(memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, methodRecord);
            
            var methodSignatureVar = ctx.DefinitionVariables.GetMethodVariable(methodSymbol.AsMethodVariable(VariableMemberKind.MethodSignature));
            Debug.Assert(methodSignatureVar.IsValid);
            
            var methodDefVar = context.Naming.SyntheticVariable(methodName, ElementKind.Method);
            ctx.Generate($"""
                          var {methodDefVar}  = metadata.AddMethodDefinition(
                                                    {methodModifiers},
                                                    MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                    metadata.GetOrAddString("{methodName}"),
                                                    {methodSignatureVar.VariableName},
                                                    methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable}),
                                                    parameterList: {methodRecord.FirstParameterHandle});
                          """);
            
            ctx.WriteNewLine();
            
            ctx.DefinitionVariables.RegisterMethod(methodSymbol.AsMethodDefinitionVariable(methodDefVar));
            
            return methodDefVar;
        });
        
        yield break;
    }

    public IEnumerable<string> Method(IVisitorContext context,
        MemberDefinitionContext memberDefinitionContext,
        string declaringTypeName,
        string methodNameForVariableRegistration,
        string methodName,
        string methodModifiers,
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        Func<IVisitorContext, string> returnTypeResolver,
        out MethodDefinitionVariable methodDefinitionVariable)
    {
        var methodRefVar = context.MemberResolver.ResolveMethod(
                                                            declaringTypeName, 
                                                            memberDefinitionContext.ParentDefinitionVariableName, 
                                                            methodNameForVariableRegistration, 
                                                            returnTypeResolver(context), 
                                                            parameters, 
                                                            typeParameters.Count,
                                                            memberDefinitionContext.Options);

        methodDefinitionVariable = context.DefinitionVariables.FindByVariableName<MethodDefinitionVariable>(methodRefVar) ?? MethodDefinitionVariable.MethodNotFound;
        TypedContext(context).DelayedDefinitionsManager.RegisterMethodDefinition(memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, methodRecord);
            
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
                                                    methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable}),
                                                    parameterList: {methodRecord.FirstParameterHandle});
                          """);
            
            ctx.WriteNewLine();
        
            var toBeRegistered = new MethodDefinitionVariable(
                                        VariableMemberKind.Method,
                                        declaringTypeName,
                                        methodName,
                                        parameters.Select(p => p.ElementType).ToArray(),
                                        0,
                                        methodDefVar);
            
            ctx.DefinitionVariables.RegisterMethod(toBeRegistered);
            
            return methodDefVar;
        });
        
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
        
        TypedContext(context).DelayedDefinitionsManager.RegisterMethodDefinition(memberDefinitionContext.ParentDefinitionVariableName, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, methodRecord);
            
            var ctorDefVar = ctx.Naming.SyntheticVariable("ctor", ElementKind.LocalVariable);
            ctx.Generate($"""
                                   var {ctorDefVar} = metadata.AddMethodDefinition(
                                                             {(isStatic ? "MethodAttributes.Private | MethodAttributes.Static" : "MethodAttributes.Public")} | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                                             MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                             metadata.GetOrAddString("{(isStatic ? ".cctor" : ".ctor")}"),
                                                             metadata.GetOrAddBlob({parameterlessCtorSignatureVar}),
                                                             methodBodyStream.AddMethodBody({memberDefinitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable}),
                                                             parameterList: {methodRecord.FirstParameterHandle});
                                   """);
            
            ctx.WriteNewLine();
            return ctorDefVar;
        });
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterFieldDefinition(memberDefinitionContext.ParentDefinitionVariableName, memberDefinitionContext.MemberDefinitionVariableName);
        
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
                 .{typedTypeResolver.ResolveAny(fieldType, ResolveTargetKind.Field)};
                 
             var {memberDefinitionContext.MemberDefinitionVariableName} = metadata.AddFieldDefinition({fieldAttributes}, metadata.GetOrAddString("{fieldOrEvent.Name}"), metadata.GetOrAddBlob({fieldSignatureVar}));
             """
        ];
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, string declaringTypeName, string name, string fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterFieldDefinition(memberDefinitionContext.ParentDefinitionVariableName, memberDefinitionContext.MemberDefinitionVariableName);
        
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

    public DefinitionVariable LocalVariable(IVisitorContext context, string variableName, string methodDefinitionVariableName, string resolvedVarType)
    {
        var variableIndex = TypedContext(context).DelayedDefinitionsManager.RegisterLocalVariable(variableName, resolvedVarType,  (ctx, localVariableEncoderVar, localVarType) =>
        {
            context.Generate($"{localVariableEncoderVar}.AddVariable().{localVarType};");
        });

        // This is a hack. SRM accesses local variables by index, and Cecilifier does not have a way to pass that index around; it only has variable names,
        // so we register the `index` of the local variable as the name.
        // Code that emits Ldloc/Stloc/etc will pass a CilFieldHandle() with this `name` (actually the local variable index) as its value
        return context.DefinitionVariables.RegisterNonMethod(string.Empty, variableName, VariableMemberKind.LocalVariable, variableIndex.ToString());
    }

    public IEnumerable<string> Property(IVisitorContext context, string declaringTypeVariable, string declaringTypeName, string propertyDefinitionVariable, string propertyName, string propertyType)
    {
        var propertySignatureTempVar =  context.Naming.SyntheticVariable($"{propertyName}SignatureBuilder", ElementKind.LocalVariable);

        TypedContext(context).DelayedDefinitionsManager.RegisterProperty(propertyName, propertyDefinitionVariable, declaringTypeName, declaringTypeVariable, 
            static (context,  propertyName, propertyDefinitionVariable, declaringTypeName, declaringTypeVariable) =>
        {
            var getterMethodVariable = context.DefinitionVariables.GetVariable($"get_{propertyName}", VariableMemberKind.Method, declaringTypeName);
            var setterMethodVariable = context.DefinitionVariables.GetVariable($"set_{propertyName}", VariableMemberKind.Method, declaringTypeName);

            foreach (var accessor in new[] {(getterMethodVariable, "Getter"), (setterMethodVariable, "Setter") })
            {
                if (!accessor.Item1.IsValid)
                    continue;
                
                context.Generate($"""
                                  // Associate method {accessor.Item1.MemberName} with property {propertyName}
                                  metadata.AddMethodSemantics(
                                                  {propertyDefinitionVariable},
                                                  MethodSemanticsAttributes.{accessor.Item2},
                                                  {accessor.Item1.VariableName});
                                  """);
                context.WriteNewLine();
            }
            
            context.Generate($"metadata.AddPropertyMap({declaringTypeVariable}, {propertyDefinitionVariable});");
            context.WriteNewLine();
        });
        
        return [$$"""
                var {{propertySignatureTempVar}} = new BlobBuilder();
                new BlobEncoder({{propertySignatureTempVar}}).PropertySignature().Parameters(0,
                                                                    returnType => returnType.{{propertyType}},
                                                                    parameters =>  {});
                
                var {{propertyDefinitionVariable}} = metadata.AddProperty(PropertyAttributes.None, metadata.GetOrAddString("{{propertyName}}"), metadata.GetOrAddBlob({{propertySignatureTempVar}}));
                """];
    }

    private SystemReflectionMetadataContext TypedContext(IVisitorContext context) => ((SystemReflectionMetadataContext) context);
    
    static void EmitLocalVariables(SystemReflectionMetadataContext context, MethodDefinitionRecord methodRecord)
    {
        var locals = methodRecord.LocalVariables;
        if (locals.Count == 0)
            return;

        var encoderVar = context.Naming.SyntheticVariable("localVariablesEncoder", ElementKind.LocalVariable);
        context.Generate($"LocalVariablesEncoder {encoderVar} = new BlobEncoder(new BlobBuilder()).LocalVariableSignature({locals.Count});");
        context.WriteNewLine();
        
        foreach (var local in locals)
        {
            local.EmitFunction(context, encoderVar, local.Type);
        }
        
        methodRecord.LocalSignatureHandleVariable = context.Naming.SyntheticVariable("localSignatureHandle", ElementKind.LocalVariable);
        context.Generate($"var {methodRecord.LocalSignatureHandleVariable} = metadata.AddStandaloneSignature(metadata.GetOrAddBlob({encoderVar}.Builder));");
        context.WriteNewLine();
    }


    static string Format(CecilifierInterpolatedStringHandler cecilFormattedString) => cecilFormattedString.Result;
}
