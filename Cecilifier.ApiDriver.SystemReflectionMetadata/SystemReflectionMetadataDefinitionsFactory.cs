using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Cecilifier.ApiDriver.SystemReflectionMetadata.CustomAttributes;
using Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;
using Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.ApiDriver.DefinitionsFactory;
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
                        MemberDefinitionContext definitionContext, 
                        string typeNamespace, string attrs, 
                        ResolvedType baseType, 
                        bool isStructWithNoFields, 
                        IEnumerable<ITypeSymbol> interfaces,
                        IEnumerable<TypeParameterSyntax>? ownTypeParameters, 
                        IEnumerable<TypeParameterSyntax> outerTypeParameters, 
                        params TypeLayoutProperty[] properties)
    {
        var typeVar = definitionContext.DefinitionVariable;
        var resolutionScope = definitionContext.ParentDefinitionVariable ?? "mainModuleHandle";
        yield return Format($"""
                      // Add a type reference for the new type. Types/Member references to the new type uses this.
                      var {typeVar} = metadata.AddTypeReference({resolutionScope}, metadata.GetOrAddString("{typeNamespace}"), metadata.GetOrAddString("{definitionContext.Name}"));
                      """);

        // We need to pass the handle of the 1st field/method defined in the module so we need to postpone the type generation after we have visited
        // all types/members.
        TypedContext(context).DelayedDefinitionsManager.RegisterTypeDefinition(typeVar, $"{typeNamespace}.{definitionContext.Name}", DefineDelayed);
        void DefineDelayed(SystemReflectionMetadataContext ctx, ref TypeDefinitionRecord typeRecord)
        {
            string? firstFieldHandle = null;
            for(int i = 0; i < typeRecord.Fields.Count; i++)
            {
                var field = typeRecord.Fields[i];
                
                var fieldHandle = field.DefinitionFunction(field);
                firstFieldHandle ??= fieldHandle;
            }
            
            typeRecord.TypeDefinitionVariable = ctx.Naming.Type(definitionContext.Identifier, ElementKind.Class);
            ctx.Generate(Format($"""
                                 var {typeRecord.TypeDefinitionVariable} = metadata.AddTypeDefinition(
                                                                  {attrs},
                                                                  metadata.GetOrAddString("{typeNamespace}"),
                                                                  metadata.GetOrAddString("{definitionContext.Name}"),
                                                                  {baseType.Expression ?? "default" },
                                                                  fieldList: {firstFieldHandle ?? ApiDriverConstants.FieldDefinitionTableNextAvailableEntry},
                                                                  methodList: {typeRecord.FirstMethodHandle ?? ApiDriverConstants.MethodDefinitionTableNextAvailableEntry});
                                 """));
            context.WriteNewLine();
            
            // Add attributes to the type definition
            foreach (var attributeEmitter in typeRecord.Attributes)
            {
                attributeEmitter(ctx, typeRecord.TypeDefinitionVariable);
            }
            
            foreach (var property in typeRecord.Properties)
            {
                // process each property passing the type definition variable (as opposed to the type reference variable) 
                property.Processor(context, property.Name, property.DefinitionVariable, property.DeclaringTypeName, typeRecord.TypeDefinitionVariable);
            }
            
            var firstProperty = typeRecord.Properties.FirstOrDefault();
            if (firstProperty.IsValid)
            {
                context.Generate($"metadata.AddPropertyMap({typeRecord.TypeDefinitionVariable}, {firstProperty.DefinitionVariable});");
                context.WriteNewLine();
            }
            
            if (definitionContext.ParentDefinitionVariable != null)
            {
                var parentTypeDefinitionVariable =  ctx.DelayedDefinitionsManager.GetTypeDefinitionVariableFromTypeReferenceVariable(definitionContext.ParentDefinitionVariable);
                context.Generate($"metadata.AddNestedType({typeRecord.TypeDefinitionVariable}, {parentTypeDefinitionVariable});"); // type is an inner type
                context.WriteNewLine();
            }

            if (properties.Length > 0)
            {
                
                var packingSize = properties.SingleOrDefault(p => p.Kind == TypeLayoutPropertyKind.PackingSize).Value;
                var clasSize = properties.SingleOrDefault(p => p.Kind == TypeLayoutPropertyKind.ClassSize).Value;
                
                context.Generate($"metadata.AddTypeLayout({typeRecord.TypeDefinitionVariable}, {packingSize}, {clasSize});");
                context.WriteNewLine();
            }
            
            foreach(var itf in interfaces)
            {
                context.Generate($"metadata.AddInterfaceImplementation({typeRecord.TypeDefinitionVariable}, {context.TypeResolver.ResolveAny(itf, ResolveTargetKind.TypeReference)});");
                context.WriteNewLine();
            }
            context.WriteNewLine();
        }
    }

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, BodiedMemberDefinitionContext bodiedMemberDefinitionContext, string methodName, string methodModifiers, IList<TypeParameterSyntax> typeParameters)
    {
        // Resolve the method to make sure there's a method ref available (this will be used to fulfill any references to this method)
        context.MemberResolver.ResolveMethod(methodSymbol);
        
        var paramIndexOffset = methodSymbol.IsStatic ? 0 : 1;
        // register all parameters so we can reference them when emitting the method body
        foreach (var parameter in methodSymbol.Parameters)
        {
            // This is a hack. SRM accesses parameters by index, and Cecilifier does not have a way to pass that index around; it only has variable names,
            // so we record the `index` of the parameter as the variable name.
            // Code that emits Ldarg/Starg/etc will use this `name` (actually the parameter index) as its operand (this is similar to the way we handle local variables)
            context.DefinitionVariables.RegisterNonMethod(methodSymbol.ToDisplayString(), parameter.Name, VariableMemberKind.Parameter, (parameter.Ordinal + paramIndexOffset).ToString());
        }

        var memberParentDefinitionVariable = bodiedMemberDefinitionContext.Member.ParentDefinitionVariable ?? throw new ArgumentNullException(nameof(bodiedMemberDefinitionContext.Member.ParentDefinitionVariable));
        TypedContext(context).DelayedDefinitionsManager.RegisterMethodDefinition(memberParentDefinitionVariable, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, bodiedMemberDefinitionContext.Member.Identifier, in methodRecord);
            
            var methodSignatureVar = ctx.DefinitionVariables.GetMethodVariable(methodSymbol.AsMethodVariable(VariableMemberKind.MethodSignature));
            Debug.Assert(methodSignatureVar.IsValid);
            
            var methodDefVar = bodiedMemberDefinitionContext.IlContext!.AssociatedMethodVariable;
            var bodyOffset = methodSymbol.ContainingType.TypeKind == TypeKind.Interface 
                                            ? "-1" 
                                            : $"methodBodyStream.AddMethodBody({bodiedMemberDefinitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable})";
            
            var firstParameterHandle = AddParametersMetadata(ctx, methodSymbol.Parameters.Select(p => p.Name));
            ctx.Generate($"""
                          var {methodDefVar}  = metadata.AddMethodDefinition(
                                                    {methodModifiers},
                                                    MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                    metadata.GetOrAddString("{methodName}"),
                                                    {methodSignatureVar.VariableName},
                                                    {bodyOffset},
                                                    parameterList: {firstParameterHandle});
                          """);
            ctx.WriteNewLine();
            ctx.WriteNewLine();

            ctx.DefinitionVariables.ExecuteDependentRegistrations(methodDefVar);
            
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
        Func<IVisitorContext, ResolvedType> returnTypeResolver,
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
        
        // register all parameters so we can reference them when emitting the method body
        for (int i = 0; i < parameters.Count; i++)
        {
            context.DefinitionVariables.RegisterNonMethod(definitionContext.Member.ContainingTypeName,  parameters[i].Name, VariableMemberKind.Parameter, $"{i + 1}");
        }

        TypedContext(context).DelayedDefinitionsManager.RegisterMethodDefinition(definitionContext.Member.ParentDefinitionVariable, (ctx, methodRecord) =>
        {
            EmitLocalVariables(ctx, definitionContext.Member.Identifier, in methodRecord);
            
            var methodReferenceToFind = new MethodDefinitionVariable(
                                                VariableMemberKind.MethodSignature,
                                                declaringTypeName,
                                                definitionContext.Member.Identifier,
                                                parameters.Select(p => p.ElementType.Expression).ToArray(),
                                                typeParameters.Count);

            var methodSignatureVar = ctx.DefinitionVariables.GetMethodVariable(methodReferenceToFind);
            Debug.Assert(methodSignatureVar.IsValid);
            
            var methodDefVar = definitionContext.Member.DefinitionVariable;
            var firstParameterHandle = AddParametersMetadata(ctx, parameters.Select(p => p.Name));

            var methodBodyOffset = definitionContext.IlContext != null
                                                ? $"methodBodyStream.AddMethodBody({definitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable})"
                                                : "-1"; // ilcontext is null meaning the method don't have a body whence we need to set offset to -1
            
            ctx.Generate($"""
                          var {methodDefVar}  = metadata.AddMethodDefinition(
                                                    {methodModifiers},
                                                    MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                    metadata.GetOrAddString("{definitionContext.Member.Name}"),
                                                    {methodSignatureVar.VariableName},
                                                    {methodBodyOffset},
                                                    parameterList: {firstParameterHandle});
                          """);
            ctx.WriteNewLine();
            ctx.WriteNewLine();
            
            var toBeRegistered = new MethodDefinitionVariable(
                                        VariableMemberKind.Method,
                                        declaringTypeName,
                                        definitionContext.Member.Name,
                                        parameters.Select(p => p.ElementType.Expression).ToArray(),
                                        0,
                                        methodDefVar);
            
            ctx.DefinitionVariables.RegisterMethod(toBeRegistered);
            ctx.DefinitionVariables.ExecuteDependentRegistrations(methodDefVar);
            
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
                                                             {(isStatic ? "MethodAttributes.Private | MethodAttributes.Static" : methodAccessibility)} | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                                             MethodImplAttributes.IL | MethodImplAttributes.Managed,
                                                             metadata.GetOrAddString("{(isStatic ? ".cctor" : ".ctor")}"),
                                                             metadata.GetOrAddBlob({parameterlessCtorSignatureVar}),
                                                             methodBodyStream.AddMethodBody({definitionContext.IlContext.VariableName}, localVariablesSignature: {methodRecord.LocalSignatureHandleVariable}),
                                                             parameterList: {methodRecord.FirstParameterHandle});
                                   """);
            
            ctx.WriteNewLine();
            ctx.WriteNewLine();
            return ctorDefVar;
        });
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, in FieldInitializationData initializer = default)
    {
        var resolvedType = context.TypeResolver.ResolveAny(fieldType, new TypeResolutionContext(ResolveTargetKind.Field, fieldType.ElementTypeSymbolOf().IsValueType ? TypeResolutionOptions.IsValueType : TypeResolutionOptions.None));
        return Field(context, definitionContext, fieldOrEvent.ContainingType.ToDisplayString(), resolvedType,  fieldAttributes, isVolatile, isByRef, initializer);
    }

    public IEnumerable<string> Field(IVisitorContext context, MemberDefinitionContext definitionContext, string declaringTypeName, ResolvedType fieldType, string fieldAttributes, bool isVolatile, bool isByRef, FieldInitializationData initializer = default)
    {
        Debug.Assert(definitionContext.ParentDefinitionVariable != null);
        
        var fieldSignatureVar = context.Naming.SyntheticVariable($"{definitionContext.Identifier}_Signature", ElementKind.Field);
        var fieldEncoderVar = context.Naming.SyntheticVariable($"{definitionContext.Identifier}_Encoder", ElementKind.Field);
        
        Buffer16<string> exps = new();
        byte expCount = 0;
        exps[expCount++] = Format($"""
                                   BlobBuilder {fieldSignatureVar} = new();
                                   var {fieldEncoderVar} = new BlobEncoder({fieldSignatureVar}).Field();
                                   """);
        if (isVolatile)
        {
            var details = fieldType.GetDetails<ResolvedTypeDetails>();
            var signatureTypeEncoderVar =  context.Naming.SyntheticVariable($"{definitionContext.Identifier}_TypeEncoder", ElementKind.Field);
            exps[expCount++] = Format($"""
                                       var {signatureTypeEncoderVar} = {fieldEncoderVar}.{details.TypeEncoderProvider};
                                       {signatureTypeEncoderVar}.CustomModifiers().AddModifier({context.TypeResolver.Resolve(context.RoslynTypeSystem.ForType(typeof(IsVolatile).FullName), ResolveTargetKind.TypeReference)}, isOptional: false);
                                       {signatureTypeEncoderVar}.{details.MethodBuilder};
                                       """);
        }
        else
        {
            exps[expCount++] = Format($"{fieldEncoderVar}.{fieldType.Expression};");
        }
        
        //Define a field reference and register it.
        exps[expCount++] = Format($"""var {definitionContext.DefinitionVariable} = metadata.AddMemberReference({definitionContext.ParentDefinitionVariable}, metadata.GetOrAddString("{definitionContext.Name}"), metadata.GetOrAddBlob({fieldSignatureVar}));""");
        context.DefinitionVariables.RegisterNonMethod(declaringTypeName, definitionContext.Name, VariableMemberKind.Field, definitionContext.DefinitionVariable);
        
        var toAdd = new FieldDefinitionRecord(fieldRecord =>
        {
            Buffer16<string> expsDef = new();
            byte expCountDef = 0;

            var fieldVariableName = context.Naming.SyntheticVariable($"{definitionContext.Identifier}", ElementKind.Field);
            var varPrefix = fieldRecord.Index == 0 || initializer || fieldRecord.Attributes.Count > 0 ? $"var {fieldVariableName} = " : "";
            expsDef[expCountDef++] = Format($"""{varPrefix}metadata.AddFieldDefinition({fieldAttributes}, metadata.GetOrAddString("{definitionContext.Name}"), metadata.GetOrAddBlob({fieldSignatureVar}));""");
            if (initializer.ConstantValue != null)
            {
                expsDef[expCountDef++] = $"metadata.AddConstant({fieldVariableName}, {initializer.ConstantValue});{Environment.NewLine}";
            }
            else if (initializer.InitializationData?.Length > 0)
            {
                var initializationByteArrayAsString = new StringBuilder();
                foreach (var itemValue in initializer.InitializationData)
                {
                    initializationByteArrayAsString.Append($"0x{itemValue:x2},");
                }

                expsDef[expCountDef++] = $"metadata.AddFieldRelativeVirtualAddress({fieldVariableName}, mappedFieldData.Count);{Environment.NewLine}";
                expsDef[expCountDef++] = $"mappedFieldData.WriteBytes((ImmutableArray<byte>) [{initializationByteArrayAsString}]);{Environment.NewLine}";
            }
            
            Span<string> span = expsDef;
            context.Generate(span.Slice(0, expCountDef).ToArray());
            foreach (var attributeEmitter in fieldRecord.Attributes)
            {
                attributeEmitter(context, fieldVariableName);
            }
            
            return fieldRecord.Index == 0 ? fieldVariableName : null;
        });

        TypedContext(context).DelayedDefinitionsManager.RegisterFieldDefinition(definitionContext.ParentDefinitionVariable, toAdd);
        
        Span<string> span = exps;
        return span.Slice(0, expCount).ToArray();
    }

    public IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, ResolvedType[] localVariableTypes, InstructionRepresentation[] instructions) => [];

    public DefinitionVariable LocalVariable(IVisitorContext context, string variableName, string methodDefinitionVariableName, ResolvedType resolvedVarType)
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

    public IEnumerable<string> Property(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string declaringTypeName, List<ParameterSpec> propertyParameters, ResolvedType propertyType)
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
                
                context.DefinitionVariables.ExecuteDependentRegistrations(propertyDefinitionVariable);
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
            var attributeEncoderVariableName = context.Naming.SyntheticVariable($"{attributeVarBaseName}_blobEncoder", ElementKind.MemberReference);
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
        if (targetKind == VariableMemberKind.Type || targetKind == VariableMemberKind.Field)
        {
            TypedContext(context).DelayedDefinitionsManager.AddAttributeEmitterToCurrentMember(targetKind, (ctx, typeDefinitionVariable) =>
            {
                AddAttributeTo(ctx, typeDefinitionVariable, resolvedAttrCtor, attributeEncoderVariable);
            });
        }
        else
        {
            // in cases which the target of the attribute is not a type/field (i.e. it is a method, etc), the target member
            // may not have been processed yet (i.e. we only have a type/member reference at this point), so a callback is
            // registered to be invoked when the that member is processed and it's safe to reference its registered variable.
            // The same approach could be used with types/fields but that would require that the variable name used to hold the
            // type definition to be known up-front adding complexity, so in that case we simply delegate to `DelayedDefinitionManager`
            context.DefinitionVariables.RegisterDependentOnRegistration(attributeTargetVar, context, (ctx, state) =>
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
                                         value: metadata.GetOrAddBlob({attributeEncoderVariable}.Builder));
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
        var parameterList = parameters.ToList();
        if (parameterList.Count == 0)
            return "MetadataTokens.ParameterHandle(metadata.GetRowCount(TableIndex.Param) + 1)";

        var firstParameterHandle = ctx.Naming.SyntheticVariable(parameterList[0], ElementKind.Parameter);
        for(int i = 0; i < parameterList.Count; i++)
        {
            if (i == 0)
                ctx.Generate($"""var {firstParameterHandle} = metadata.AddParameter(ParameterAttributes.None, metadata.GetOrAddString("{parameterList[i]}"), {i+1});""");
            else
                ctx.Generate($"""metadata.AddParameter(ParameterAttributes.None, metadata.GetOrAddString("{parameterList[i]}"), {i+1});""");
            ctx.WriteNewLine();
        }
        ctx.WriteNewLine();
        return firstParameterHandle;
    }

    static string Format(CecilifierInterpolatedStringHandler cecilFormattedString) => cecilFormattedString.Result;
}

file record struct NonTypeAttributeTargetState (string AttributeTarget, string ResolvedAttributeCtor, string AttributeEncoderVariable);
