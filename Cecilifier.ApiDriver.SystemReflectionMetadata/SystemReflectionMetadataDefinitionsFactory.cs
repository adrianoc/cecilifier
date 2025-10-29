using System.Diagnostics;
using Cecilifier.ApiDriver.SystemReflectionMetadata.CustomAttributes;
using Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
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
                      var {typeVar} = metadata.AddTypeReference(mainModuleHandle, metadata.GetOrAddString("{typeNamespace}"), metadata.GetOrAddString("{typeName}"));
                      """);

        // We need to pass the handle of the 1st field/method defined in the module so we need to postpone the type generation after we have visited
        // all types/members.
        TypedContext(context).DelayedDefinitionsManager.RegisterTypeDefinition(typeVar, $"{typeNamespace}.{typeName}", DefineDelayed);
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

            // Add attributes to the type definition
            foreach (var attributeEmitter in typeRecord.Attributes)
            {
                attributeEmitter(ctx, typeDefVar);    
            }
            
            foreach (var property in typeRecord.Properties)
            {
                // process each property passing the type definition variable (as opposed to the type reference variable) 
                property.Processor(context, property.Name, property.DefinitionVariable, property.DeclaringTypeName, typeDefVar);
            }
            
            var firstProperty = typeRecord.Properties.FirstOrDefault();
            if (firstProperty.IsValid)
            {
                context.Generate($"metadata.AddPropertyMap({typeDefVar}, {firstProperty.DefinitionVariable});");
                context.WriteNewLine();
            }
        }
    }

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, BodiedMemberDefinitionContext bodiedMemberDefinitionContext, string methodName, string methodModifiers, IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters)
    {
        // Resolve the method to make sure there's a method ref available (this will be used to fulfill any references to this method)
        context.MemberResolver.ResolveMethod(methodSymbol);

        var memberParentDefinitionVariable = bodiedMemberDefinitionContext.Member.ParentDefinitionVariable ?? throw new ArgumentNullException(nameof(bodiedMemberDefinitionContext.Member.ParentDefinitionVariable));
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterMethodDefinition(memberParentDefinitionVariable, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, bodiedMemberDefinitionContext.Member.Identifier, in methodRecord);
            
            var methodSignatureVar = ctx.DefinitionVariables.GetMethodVariable(methodSymbol.AsMethodVariable(VariableMemberKind.MethodSignature));
            Debug.Assert(methodSignatureVar.IsValid);
            
            var methodDefVar = context.Naming.SyntheticVariable(bodiedMemberDefinitionContext.Member.Identifier, ElementKind.Method);
            ctx.Generate($"""
                          var {methodDefVar}  = metadata.AddMethodDefinition(
                                                    {methodModifiers},
                                                    MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                    metadata.GetOrAddString("{methodName}"),
                                                    {methodSignatureVar.VariableName},
                                                    methodBodyStream.AddMethodBody({bodiedMemberDefinitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable}),
                                                    parameterList: {methodRecord.FirstParameterHandle});
                          """);
            
            ctx.WriteNewLine();
            
            ctx.DefinitionVariables.RegisterMethod(methodSymbol.AsMethodDefinitionVariable(methodDefVar));
            
            return methodDefVar;
        });
        
        yield break;
    }

    public IEnumerable<string> Method(IVisitorContext context,
        BodiedMemberDefinitionContext definitionContext,
        string declaringTypeName,
        string methodModifiers,
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        Func<IVisitorContext, string> returnTypeResolver,
        out MethodDefinitionVariable methodDefinitionVariable)
    {
        var methodRefVar = context.MemberResolver.ResolveMethod(
                                                            declaringTypeName, 
                                                            definitionContext.Member.ParentDefinitionVariable, 
                                                            definitionContext.Member.Identifier, 
                                                            returnTypeResolver(context), 
                                                            parameters, 
                                                            typeParameters.Count,
                                                            definitionContext.Options);

        methodDefinitionVariable = context.DefinitionVariables.FindByVariableName<MethodDefinitionVariable>(methodRefVar) ?? MethodDefinitionVariable.MethodNotFound;
        TypedContext(context).DelayedDefinitionsManager.RegisterMethodDefinition(definitionContext.Member.ParentDefinitionVariable, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, definitionContext.Member.Identifier, in methodRecord);
            
            var methodReferenceToFind = new MethodDefinitionVariable(
                                                VariableMemberKind.MethodSignature,
                                                declaringTypeName,
                                                definitionContext.Member.Identifier,
                                                parameters.Select(p => p.ElementType).ToArray(),
                                                typeParameters.Count);

            var methodSignatureVar = ctx.DefinitionVariables.GetMethodVariable(methodReferenceToFind);
            Debug.Assert(methodSignatureVar.IsValid);
            
            var methodDefVar = definitionContext.Member.DefinitionVariable;
            var firstParameterHandle = AddParametersMetadata(ctx, parameters.Select(p => p.Name));

            ctx.Generate($"""
                          var {methodDefVar}  = metadata.AddMethodDefinition(
                                                    {methodModifiers},
                                                    MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                    metadata.GetOrAddString("{definitionContext.Member.Name}"),
                                                    {methodSignatureVar.VariableName},
                                                    methodBodyStream.AddMethodBody({definitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable}),
                                                    parameterList: {firstParameterHandle});
                          """);
            ctx.WriteNewLine();
            
            var toBeRegistered = new MethodDefinitionVariable(
                                        VariableMemberKind.Method,
                                        declaringTypeName,
                                        definitionContext.Member.Name,
                                        parameters.Select(p => p.ElementType).ToArray(),
                                        0,
                                        methodDefVar);
            
            ctx.DefinitionVariables.RegisterMethod(toBeRegistered);
            
            return methodDefVar;
        });
        
        return [];
    }

    public IEnumerable<string> Constructor(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null)
    {
        var parameterlessCtorSignatureVar = context.Naming.SyntheticVariable($"{typeName}_ctorSignature", ElementKind.MemberReference);
        yield return Format(
            $$"""
              var {{parameterlessCtorSignatureVar}} = new BlobBuilder();
              new BlobEncoder({{parameterlessCtorSignatureVar}})
                     .MethodSignature(isInstanceMethod: {{ (!isStatic).ToKeyword()}})
                     .Parameters(0, returnType => returnType.Void(), parameters => { });
              """);

        var parentDefinitionVariable = definitionContext.Member.ParentDefinitionVariable ?? throw new ArgumentNullException(nameof(definitionContext.Member.ParentDefinitionVariable));
        TypedContext(context).DelayedDefinitionsManager.RegisterMethodDefinition(parentDefinitionVariable, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, "ctor", in methodRecord);
            
            var ctorDefVar = ctx.Naming.SyntheticVariable($"{typeName}_Ctor", ElementKind.MemberReference);
            ctx.Generate($"""
                                   var {ctorDefVar} = metadata.AddMethodDefinition(
                                                             {(isStatic ? "MethodAttributes.Private | MethodAttributes.Static" : "MethodAttributes.Public")} | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                                             MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                             metadata.GetOrAddString("{(isStatic ? ".cctor" : ".ctor")}"),
                                                             metadata.GetOrAddBlob({parameterlessCtorSignatureVar}),
                                                             methodBodyStream.AddMethodBody({definitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable}),
                                                             parameterList: {methodRecord.FirstParameterHandle});
                                   """);
            
            ctx.WriteNewLine();
            return ctorDefVar;
        });
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        return Field(context, definitionContext, fieldOrEvent.ContainingType.ToDisplayString(), fieldOrEvent.Name, context.TypeResolver.ResolveAny(fieldType, ResolveTargetKind.Field),  fieldAttributes, isVolatile, isByRef, constantValue);
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, string declaringTypeName, string name, string fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        Debug.Assert(definitionContext.ParentDefinitionVariable != null);
        ((SystemReflectionMetadataContext) context).DelayedDefinitionsManager.RegisterFieldDefinition(definitionContext.ParentDefinitionVariable, definitionContext.DefinitionVariable);
        
        context.DefinitionVariables.RegisterNonMethod(declaringTypeName, name, VariableMemberKind.Field, definitionContext.DefinitionVariable);
        var fieldSignatureVar = context.Naming.SyntheticVariable($"{definitionContext.Identifier}_Signature", ElementKind.MemberReference);
        Buffer256<string> exps = new();
        byte expCount = 0;
        exps[expCount++] = Environment.NewLine;
        exps[expCount++] = Format($"""
                             BlobBuilder {fieldSignatureVar} = new();
                             new BlobEncoder({fieldSignatureVar})
                                 .Field()
                                 .{fieldType};
                                 
                             var {definitionContext.DefinitionVariable} = metadata.AddFieldDefinition({fieldAttributes}, metadata.GetOrAddString("{name}"), metadata.GetOrAddBlob({fieldSignatureVar}));{Environment.NewLine}
                             """);
        
        if (constantValue != null)
        {
            exps[expCount++] = $"metadata.AddConstant({definitionContext.DefinitionVariable}, {constantValue});{Environment.NewLine}";
        }

        Span<string> span = exps;
        return span.Slice(0, expCount).ToArray();
    }

    public IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, string[] localVariableTypes, InstructionRepresentation[] instructions) => [];

    public DefinitionVariable LocalVariable(IVisitorContext context, string variableName, string methodDefinitionVariableName, string resolvedVarType)
    {
        var variableIndex = TypedContext(context).DelayedDefinitionsManager.RegisterLocalVariable(variableName, resolvedVarType,  (ctx, localVariableEncoderVar, localVarType) =>
        {
            context.Generate($"{localVariableEncoderVar}.AddVariable().{localVarType};");
        });

        // This is a hack. SRM accesses local variables by index, and Cecilifier does not have a way to pass that index around; it only has variable names,
        // so we record the `index` of the local variable as the variable name.
        // Code that emits Ldloc/Stloc/etc will pass a CilFieldHandle() with this `name` (actually the local variable index) as its value
        return context.DefinitionVariables.RegisterNonMethod(string.Empty, variableName, VariableMemberKind.LocalVariable, variableIndex.ToString());
    }

    public IEnumerable<string> Property(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string declaringTypeName, List<ParameterSpec> propertyParameters, string propertyType)
    {
        var propertySignatureTempVar =  context.Naming
                                                    .With(NamingOptions.NoCasingElementNames)
                                                    .Without(NamingOptions.CamelCaseElementNames).SyntheticVariable($"{definitionContext.Member.Name.CamelCase()}_blobBuilder", ElementKind.MemberReference);

        TypedContext(context).DelayedDefinitionsManager.RegisterProperty(definitionContext.Member.Name, definitionContext.Member.DefinitionVariable, declaringTypeName, definitionContext.Member.ParentDefinitionVariable!, 
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
        });
        
        return [Format($$"""
                var {{propertySignatureTempVar}} = new BlobBuilder();
                new BlobEncoder({{propertySignatureTempVar}}).PropertySignature(isInstanceProperty: {{((definitionContext.Options & MemberOptions.Static) == 0).ToKeyword()}}).Parameters({{propertyParameters.Count}},
                                                                    returnType => returnType.{{propertyType}},
                                                                    parameters =>  
                                                                    {
                                                                        {{ string.Join('\n', propertyParameters.Select(p => $"parameters.AddParameter().{p.ElementType};")) }}
                                                                    });
                
                var {{definitionContext.Member.DefinitionVariable}} = metadata.AddProperty(PropertyAttributes.None, metadata.GetOrAddString("{{definitionContext.Member.Name}}"), metadata.GetOrAddBlob({{propertySignatureTempVar}}));
                """)];
    }

    public IEnumerable<string> Attribute(IVisitorContext context, IMethodSymbol attributeCtor, string attributeVarBaseName, string attributeTargetVar, VariableMemberKind targetKind, params CustomAttributeArgument[] arguments)
    {
        var attributeEncoderVariable = context.DefinitionVariables.GetVariable("EncoderMetaName", VariableMemberKind.None, attributeVarBaseName);

        if (!attributeEncoderVariable.IsValid)
        {
            var attributeEncoderVariableName = context.Naming.SyntheticVariable($"{attributeVarBaseName}_blobBuilder", ElementKind.MemberReference);
            var attributeEncoder = new AttributeEncoder(context, attributeEncoderVariableName, attributeCtor.ContainingType.Name);
            var namedArguments = arguments.OfType<CustomAttributeNamedArgument>().ToList();
            var encodedArguments = attributeEncoder.Encode(arguments.Except(namedArguments).ToList(), namedArguments);
            yield return Format($"""
                             // encoded attribute arguments
                             { encodedArguments }
                             """);

            attributeEncoderVariable = context.DefinitionVariables.RegisterNonMethod(attributeVarBaseName, "EncoderMetaName", VariableMemberKind.None, attributeEncoderVariableName);
        }
        
        var resolvedAttrCtor = context.MemberResolver.ResolveMethod(attributeCtor);

        if (targetKind == VariableMemberKind.Type)
        {
            TypedContext(context).DelayedDefinitionsManager.AddAttributeToCurrentType((ctx, typeDefinitionVariable) =>
            {
                AddAttributeTo(ctx, typeDefinitionVariable, resolvedAttrCtor, attributeEncoderVariable);
            });
        }
        else
        {
            // in cases which the attribute is being applied not to a type (a method, a field, etc), the target member may not yet b processed
            // so a callback is registered to be invoked when the tha member is processed and its associated variable is registered.
            // The same approach could be used with types but that would require that the variable used to hold the type definition to be
            // defined up-front which would add more complexity so in that case we simply delegate to `DelayedDefinitionManager`
            context.DefinitionVariables.ExecuteUponVariableRegistration(attributeTargetVar, context, (ctx, state) =>
            {
                var target = (NonTypeAttributeTargetState) state;
                AddAttributeTo(ctx, target.AttributeTarget, target.ResolvedAttributeCtor, target.AttributeEncoderVariable);
            }, new NonTypeAttributeTargetState(attributeTargetVar, resolvedAttrCtor, attributeEncoderVariable.VariableName));
        }

        static void AddAttributeTo(IVisitorContext ctx, string targetVariable, string resolvedAttributeCtor, string attributeEncoderVariable)
        {
            var attr= Format($"""
                              metadata.AddCustomAttribute(
                                         parent: {targetVariable},
                                         constructor: {resolvedAttributeCtor},
                                         value: metadata.GetOrAddBlob({attributeEncoderVariable}));
                              """);
            ctx.Generate(attr);
            ctx.WriteNewLine();
        }
    }

    private SystemReflectionMetadataContext TypedContext(IVisitorContext context) => ((SystemReflectionMetadataContext) context);
    
    static void EmitLocalVariables(SystemReflectionMetadataContext context, string methodName, ref readonly MethodDefinitionRecord methodRecord)
    {
        var locals = methodRecord.LocalVariables;
        if (locals.Count == 0)
            return;

        var encoderVar = context.Naming.SyntheticVariable($"{methodName}_Encoder", ElementKind.LocalVariable);
        context.Generate($"LocalVariablesEncoder {encoderVar} = new BlobEncoder(new BlobBuilder()).LocalVariableSignature({locals.Count});");
        context.WriteNewLine();
        
        foreach (var local in locals)
        {
            local.EmitFunction(context, encoderVar, local.Type);
        }
        
        methodRecord.LocalSignatureHandleVariable = context.Naming.SyntheticVariable($"{methodName}_Signature", ElementKind.LocalVariable);
        context.WriteNewLine();
        context.Generate($"var {methodRecord.LocalSignatureHandleVariable} = metadata.AddStandaloneSignature(metadata.GetOrAddBlob({encoderVar}.Builder));");
        context.WriteNewLine();
    }

    private static string AddParametersMetadata(SystemReflectionMetadataContext ctx, IEnumerable<string> parameters)
    {
        
        var firstParameterHandle = "MetadataTokens.ParameterHandle(metadata.GetRowCount(TableIndex.Param) + 1)";
        int i = 1;
        foreach (var p in parameters)
        {
            if (i == 1)
            {
                firstParameterHandle = ctx.Naming.SyntheticVariable(p, ElementKind.Parameter);
                ctx.Generate($"var {firstParameterHandle} = ");
            }
            ctx.Generate($"""metadata.AddParameter(ParameterAttributes.None, metadata.GetOrAddString("{p}"), {i++});""");
            ctx.WriteNewLine();
        }
        
        return firstParameterHandle;
    }

    static string Format(CecilifierInterpolatedStringHandler cecilFormattedString) => cecilFormattedString.Result;
}

file record struct NonTypeAttributeTargetState (string AttributeTarget, string ResolvedAttributeCtor, string AttributeEncoderVariable);
