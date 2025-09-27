#nullable enable annotations

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.Misc
{
    public sealed class CecilDefinitionsFactory
    {
        public static string CallSite(ITypeResolver resolver, IFunctionPointerTypeSymbol functionPointer)
        {
            return FunctionPointerTypeBasedCecilType(
                resolver,
                functionPointer,
                (hasThis, parameters, returnType) => $"new CallSite({returnType}) {{ {hasThis}, {parameters} }}");
        }

        public static string FunctionPointerType(ITypeResolver resolver, IFunctionPointerTypeSymbol functionPointer)
        {
            return FunctionPointerTypeBasedCecilType(
                resolver,
                functionPointer,
                (hasThis, parameters, returnType) => $"new FunctionPointerType() {{ {hasThis}, ReturnType = {returnType}, {parameters} }}");
        }

        public static IEnumerable<string> Method(IVisitorContext context, string methodVar, string methodName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
        {
            var exps = new List<string>();

            var resolvedReturnType = context.TypeResolver.ResolveAny(returnType);
            if (refReturn)
                resolvedReturnType = resolvedReturnType.MakeByReferenceType();

            // for type parameters we may need to postpone setting the return type (using void as a placeholder, since we need to pass something) until the generic parameters has been
            // handled. This is required because the type parameter may be defined by the method being processed.
            exps.Add($"var {methodVar} = new MethodDefinition(\"{methodName}\", {methodModifiers}, {(returnType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter() ? context.TypeResolver.Bcl.System.Void : resolvedReturnType)});");
            ProcessGenericTypeParameters(methodVar, context, typeParameters, exps);
            if (returnType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter())
            {
                resolvedReturnType = context.TypeResolver.ResolveAny(returnType);
                exps.Add($"{methodVar}.ReturnType = {(refReturn ? resolvedReturnType.MakeByReferenceType() : resolvedReturnType)};");
            }

            return exps;
        }

        public static IEnumerable<string> Method(
            IVisitorContext context,
            string declaringTypeName,
            string methodVar,
            string methodNameForParameterVariableRegistration, // we can't use the method name in some scenarios (indexers, for instance) 
            string methodName,
            string methodModifiers,
            IReadOnlyList<ParameterSpec> parameters,
            IList<string> typeParameters,
            Func<IVisitorContext, string> returnTypeResolver,
            out MethodDefinitionVariable methodDefinitionVariable)
        {
            var exps = new List<string>();

            // if the method has type parameters we need to postpone setting the return type (using void as a placeholder, since we need to pass something) until the generic parameters has been
            // handled. This is required because the type parameter may be defined by the method being processed which introduces a chicken and egg problem.
            exps.Add($"var {methodVar} = new MethodDefinition(\"{methodName}\", {methodModifiers}, { (typeParameters.Count == 0 ? returnTypeResolver(context) : context.TypeResolver.Bcl.System.Void) });");
            ProcessGenericTypeParameters(methodVar, context, $"{declaringTypeName}.{methodName}", typeParameters, exps);
            if (typeParameters.Count > 0)
                exps.Add($"{methodVar}.ReturnType = {returnTypeResolver(context)};");

            foreach (var parameter in parameters)
            {
                var paramVar = context.Naming.SyntheticVariable(parameter.Name, ElementKind.Parameter);
                var parameterExp = Parameter(
                    parameter.Name, 
                    parameter.RefKind, 
                    null, // for now,the only callers for this method don't have any `params` parameters.
                    methodVar,
                    paramVar,
                    parameter.ElementTypeResolver != null ? parameter.ElementTypeResolver(context, parameter.ElementType) : parameter.ElementType,
                    parameter.Attributes,
                    (parameter.DefaultValue, parameter.DefaultValue != null));
                
                context.DefinitionVariables.RegisterNonMethod(methodNameForParameterVariableRegistration, parameter.Name, VariableMemberKind.Parameter, paramVar);
                exps.AddRange(parameterExp);
            }

            methodDefinitionVariable = context.DefinitionVariables.RegisterMethod(declaringTypeName, methodName, parameters.Select(p => p.RegistrationTypeName).ToArray(), typeParameters.Count, methodVar);
            return exps;
        }

        internal static string Constructor(IVisitorContext context, string ctorLocalVar, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null)
        {
            var ctorName = Utils.ConstructorMethodName(isStatic);
            context.DefinitionVariables.RegisterMethod(typeName, ctorName, paramTypes, 0, ctorLocalVar);

            var exp = $@"var {ctorLocalVar} = new MethodDefinition(""{ctorName}"", {methodAccessibility} | MethodAttributes.HideBySig | {Constants.Cecil.CtorAttributes}, assembly.MainModule.TypeSystem.Void)";
            if (methodDefinitionPropertyValues != null)
            {
                exp += $"{{ {methodDefinitionPropertyValues} }}";
            }

            return exp + ";";
        }

        public static IEnumerable<string> MethodBody(IVisitorContext context, string methodName, string methodVar, string[] localVariableTypes, InstructionRepresentation[] instructions)
        {
            return MethodBody(context.Naming, methodName, context.ApiDriver.NewIlContext(context, methodName, methodVar), localVariableTypes, instructions);
        }

        public static IEnumerable<string> MethodBody(INameStrategy nameStrategy, string methodName, IlContext ilContext, string[] localVariableTypes, InstructionRepresentation[] instructions)
        {
            var tagToInstructionDefMapping = new Dictionary<string, string>();
            yield return $"{ilContext.RelatedMethodVariable}.Body = new MethodBody({ilContext.RelatedMethodVariable});";
            
            if (localVariableTypes.Length > 0)
            {
                yield return $"{ilContext.RelatedMethodVariable}.Body.InitLocals = true;";
                foreach (var localVariableType in localVariableTypes)
                {
                    yield return $"{ilContext.RelatedMethodVariable}.Body.Variables.Add({LocalVariable(localVariableType)});";
                }
            }
            
            if (instructions.Length == 0)
                yield break;

            var methodInstVar = nameStrategy.SyntheticVariable(methodName, ElementKind.LocalVariable);
            yield return $"var {methodInstVar} = {ilContext.RelatedMethodVariable}.Body.Instructions;";

            // create `Mono.Cecil.Instruction` instances for each instruction that has a 'Tag'
            foreach (var inst in instructions.Where(inst => !inst.Ignore))
            {
                if (inst.Tag == null)
                    continue;
                
                var instVar = nameStrategy.SyntheticVariable(inst.Tag, ElementKind.Label);
                yield return $"var {instVar} = {ilContext.VariableName}.Create({inst.OpCode.ConstantName()}{OperandFor(inst)});";
                tagToInstructionDefMapping[inst.Tag] = instVar;
            }

            foreach (var inst in instructions.Where(inst => !inst.Ignore))
            {
                yield return inst.Tag != null 
                    ? $"{methodInstVar}.Add({tagToInstructionDefMapping[inst.Tag]});" 
                    : $"{methodInstVar}.Add({ilContext.VariableName}.Create({inst.OpCode.ConstantName()}{OperandFor(inst)}));";
            }
           

            string OperandFor(InstructionRepresentation inst)
            {
                return inst.Operand?.Insert(0, ", ")
                       ?? inst.BranchTargetTag?.Replace(inst.BranchTargetTag, $", {tagToInstructionDefMapping[inst.BranchTargetTag]}")
                       ?? string.Empty;
            }
        }

        internal static string LocalVariable(string resolvedType) => $"new VariableDefinition({resolvedType})";
        
        /*
         * Creates the snippet for a TypeDefinition.
         * 
         * Note that:
         * 1. At IL level, type parameters from *outer* types are considered to be part of a inner type whence these type parameters need to be added to the list of type parameters even
         *    if the type being declared is not a generic type.
         * 
         * 2. Only type parameters owned by the type being declared are considered when computing the arity of the type (whence the number following the backtick reflects only the
         *    # of the type parameters declared by the type being declared). 
         */
        public static IEnumerable<string> Type(
            IVisitorContext context,
            string typeVar,
            string typeNamespace,
            string typeName,
            string attrs,
            string baseTypeName,
            DefinitionVariable outerTypeVariable,
            bool isStructWithNoFields,
            IEnumerable<ITypeSymbol> interfaces,
            IEnumerable<TypeParameterSyntax>? ownTypeParameters,
            IEnumerable<TypeParameterSyntax> outerTypeParameters,
            params string[] properties)
        {
            var typeParamList = ownTypeParameters?.ToArray() ?? [];
            if (typeParamList.Length > 0)
            {
                typeName = typeName + "`" + typeParamList.Length;
            }

            var exps = new List<string>();
            var typeDefExp = $"var {typeVar} = new TypeDefinition(\"{typeNamespace}\", \"{typeName}\", {attrs}{(!string.IsNullOrWhiteSpace(baseTypeName) ? ", " + baseTypeName : "")})";
            if (properties.Length > 0)
            {
                exps.Add($"{typeDefExp} {{ {string.Join(',', properties)} }};");
            }
            else
            {
                exps.Add($"{typeDefExp};");
            }

            // add type parameters from outer types. 
            var outerTypeParametersArray = outerTypeParameters.ToArray();
            ProcessGenericTypeParameters(typeVar, context, outerTypeParametersArray.Concat(typeParamList).ToArray(), exps);
            
            foreach (var itf in interfaces)
            {
                exps.Add($"{typeVar}.Interfaces.Add(new InterfaceImplementation({context.TypeResolver.ResolveAny(itf)}));");
            }

            if (outerTypeVariable.IsValid && outerTypeVariable.VariableName != typeVar)
                exps.Add($"{outerTypeVariable.VariableName}.NestedTypes.Add({typeVar});"); // type is a inner type of *context.CurrentType* 
            else
                exps.Add($"assembly.MainModule.Types.Add({typeVar});");

            if (isStructWithNoFields)
            {
                exps.Add($"{typeVar}.ClassSize = 1;");
                exps.Add($"{typeVar}.PackingSize = 0;");
            }

            return exps;
        }

        private static string GenericParameter(IVisitorContext context, string typeParameterOwnerVar, string genericParamName, string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)
        {
            context.DefinitionVariables.RegisterNonMethod(typeParameterSymbol.ContainingSymbol.ToDisplayString(), genericParamName, VariableMemberKind.TypeParameter, genParamDefVar);
            return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeParameterOwnerVar}){Variance(typeParameterSymbol)};";
        }

        public static string GenericParameter(IVisitorContext context, string ownerContainingTypeName, string typeParameterOwnerVar, string genericParamName, string genParamDefVar)
        {
            context.DefinitionVariables.RegisterNonMethod(ownerContainingTypeName, genericParamName, VariableMemberKind.TypeParameter, genParamDefVar);
            return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeParameterOwnerVar});";
        }

        private static string Variance(ITypeParameterSymbol typeParameterSymbol)
        {
            if (typeParameterSymbol.Variance == VarianceKind.In)
            {
                return " { IsContravariant = true }";
            }

            if (typeParameterSymbol.Variance == VarianceKind.Out)
            {
                return " { IsCovariant = true }";
            }

            return string.Empty;
        }

        public static IEnumerable<string> Field(IVisitorContext context, string declaringTypeName, string declaringTypeVar, string fieldVar, string name, string fieldType, string fieldAttributes, bool isByRef, object? constantValue = null)
        {
            return context.ApiDefinitionsFactory.Field(context, new MemberDefinitionContext(fieldVar, declaringTypeVar, IlContext.None), declaringTypeName, name, fieldType, fieldAttributes, false, isByRef, constantValue);
        }

        public static string ParameterDoesNotHandleParamsKeywordOrDefaultValue(string name, RefKind byRef, string resolvedType, string? paramAttributes = null)
        {
            paramAttributes ??= Constants.ParameterAttributes.None;
            if (RefKind.None != byRef)
            {
                resolvedType = resolvedType.MakeByReferenceType();
            }

            return $"new ParameterDefinition(\"{name}\", {paramAttributes}, {resolvedType})";
        }

        public static IEnumerable<string> Parameter(string name, RefKind byRef, string? paramsAttributeTypeName, string methodVar, string paramVar, string resolvedType, string paramAttributes, (string Value, bool Present) defaultParameterValue)
        {
            var exps = new List<string>();

            exps.Add($"var {paramVar} = {ParameterDoesNotHandleParamsKeywordOrDefaultValue(name, byRef, resolvedType, paramAttributes)};");
            if (!string.IsNullOrWhiteSpace(paramsAttributeTypeName))
            {
                exps.Add($"{paramVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof({paramsAttributeTypeName}).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
            }

            if (defaultParameterValue.Present)
                exps.Add($"{paramVar}.Constant = {defaultParameterValue.Value ?? "null" };");

            exps.Add($"{methodVar}.Parameters.Add({paramVar});");

            return exps;
        }

        public static IEnumerable<string> Parameter(IVisitorContext context, ParameterSyntax node, string methodVar, string paramVar)
        {
            var paramSymbol = context.SemanticModel.GetDeclaredSymbol(node);
            TypeDeclarationVisitor.EnsureForwardedTypeDefinition(context, paramSymbol!.Type, Array.Empty<TypeParameterSyntax>());
            return Parameter(context, paramSymbol, methodVar, paramVar);
        }

        public static IEnumerable<string> Parameter(IVisitorContext context, IParameterSymbol paramSymbol, string methodVar, string paramVar)
        {
            return Parameter(
                paramSymbol.Name,
                paramSymbol.RefKind,
                paramSymbol.IsParams ? paramSymbol.Type.ParamsAttributeMatchingType() : null,
                methodVar,
                paramVar,
                context.TypeResolver.ResolveAny(paramSymbol.Type, ResolveTargetKind.Parameter, methodVar),
                paramSymbol.AsParameterAttribute(),
                paramSymbol.ExplicitDefaultValue(rawString: false));
        }

        public static IEnumerable<string> Attribute(string attrTargetVar, IVisitorContext context, AttributeSyntax attribute, Func<ITypeSymbol, AttributeArgumentSyntax[], string> ctorResolver)
        {
            var exps = new List<string>();
            var attrType = context.GetTypeInfo(attribute.Name);

            var customAttrVar = context.Naming.CustomAttribute(attribute.Name.ToSimpleName());
            var attributeArguments = attribute.ArgumentList == null
                ? Array.Empty<AttributeArgumentSyntax>()
                : attribute.ArgumentList.Arguments.Where(arg => arg.NameEquals == null).ToArray();

            var ctorExp = ctorResolver(attrType.Type, attributeArguments);
            exps.Add($"var {customAttrVar} = new CustomAttribute({ctorExp});");

            if (attribute.ArgumentList != null)
            {
                foreach (var attrArg in attributeArguments)
                {
                    var argType = context.GetTypeInfo(attrArg.Expression);
                    exps.Add($"{customAttrVar}.ConstructorArguments.Add({CustomAttributeArgument(argType, attrArg)});");
                }

                ProcessAttributeNamedArguments(SymbolKind.Property, "Properties");
                ProcessAttributeNamedArguments(SymbolKind.Field, "Fields");
            }

            exps.Add($"{attrTargetVar}.CustomAttributes.Add({customAttrVar});");

            return exps;

            string CustomAttributeArgument(TypeInfo argType, AttributeArgumentSyntax attrArg)
            {
                return $"new CustomAttributeArgument({context.TypeResolver.ResolveAny(argType.Type)}, {attrArg.Expression.EvaluateAsCustomAttributeArgument(context)})";
            }

            void ProcessAttributeNamedArguments(SymbolKind symbolKind, string container)
            {
                var attrMemberNames = attrType.Type!.GetMembers().Where(m => m.Kind == symbolKind).Select(m => m.Name).ToHashSet();
                foreach (var namedArgument in attribute.ArgumentList.Arguments.Except(attributeArguments).Where(arg => attrMemberNames.Contains(arg.NameEquals!.Name.Identifier.Text)))
                {
                    var argType = context.GetTypeInfo(namedArgument.Expression);
                    exps.Add($"{customAttrVar}.{container}.Add(new CustomAttributeNamedArgument(\"{namedArgument.NameEquals!.Name.Identifier.ValueText}\", {CustomAttributeArgument(argType, namedArgument)}));");
                }
            }
        }
        
        public static string[] Attribute(string attributeVarBaseName, string attributeTargetVar, IVisitorContext context, string resolvedCtor, params (string ResolvedType, string Value)[] parameters)
        {
            var attributeVar = context.Naming.SyntheticVariable(attributeVarBaseName, ElementKind.Attribute);

            var exps = new string[2 + parameters.Length];
            int expIndex = 0;
            exps[expIndex++] = $"var {attributeVar} = new CustomAttribute({resolvedCtor});";

            for(int i = 0; i < parameters.Length; i++)
            {
                var attributeArgument = $"new CustomAttributeArgument({parameters[i].ResolvedType}, {parameters[i].Value})";
                exps[expIndex++] = $"{attributeVar}.ConstructorArguments.Add({attributeArgument});";
            }
            exps[expIndex] = $"{attributeTargetVar}.CustomAttributes.Add({attributeVar});";

            return exps;
        }

        private static void ProcessGenericTypeParameters(string memberDefVar, IVisitorContext context, IList<TypeParameterSyntax> typeParamList, IList<string> exps)
        {
            // forward declare all generic type parameters to allow one type parameter to reference any of the others; this is useful in constraints for example:
            // class Foo<T,S> where T: S  { }
            var genericTypeParamEntries = ArrayPool<(string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)>.Shared.Rent(typeParamList.Count);
            for (int i = 0; i < typeParamList.Count; i++)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(typeParamList[i]).EnsureNotNull();
                var genericParamName = typeParamList[i].Identifier.Text;

                var genParamDefVar = context.Naming.GenericParameterDeclaration(typeParamList[i]);
                exps.Add(GenericParameter(context, memberDefVar, genericParamName, genParamDefVar, symbol));
                genericTypeParamEntries[i] = (genParamDefVar, symbol);
            }

            for (int i = 0; i < typeParamList.Count; i++)
            {
                exps.Add($"{memberDefVar}.GenericParameters.Add({genericTypeParamEntries[i].genParamDefVar});");
                AddConstraints(genericTypeParamEntries[i].genParamDefVar, genericTypeParamEntries[i].typeParameterSymbol, typeParamList[i]);
            }

            ArrayPool<(string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)>.Shared.Return(genericTypeParamEntries);

            void AddConstraints(string genParamDefVar, ITypeParameterSymbol typeParam, TypeParameterSyntax typeParameterSyntax)
            {
                if (typeParam.HasConstructorConstraint || typeParam.HasValueTypeConstraint) // struct constraint implies new()
                {
                    exps.Add($"{genParamDefVar}.HasDefaultConstructorConstraint = true;");
                }

                if (typeParam.HasReferenceTypeConstraint)
                {
                    exps.Add($"{genParamDefVar}.HasReferenceTypeConstraint = true;");
                }

                if (typeParam.HasValueTypeConstraint)
                {
                    var systemValueTypeRef = Utils.ImportFromMainModule("typeof(System.ValueType)");
                    var constraintType = typeParam.HasUnmanagedTypeConstraint
                        ? $"{systemValueTypeRef}.MakeRequiredModifierType({context.TypeResolver.ResolveAny(context.RoslynTypeSystem.ForType<System.Runtime.InteropServices.UnmanagedType>())})"
                        : systemValueTypeRef;

                    exps.Add($"{genParamDefVar}.Constraints.Add(new GenericParameterConstraint({constraintType}));");
                    exps.Add($"{genParamDefVar}.HasNotNullableValueTypeConstraint = true;");
                }

                if (typeParam.Variance == VarianceKind.In)
                {
                    exps.Add($"{genParamDefVar}.IsContravariant = true;");
                }
                else if (typeParam.Variance == VarianceKind.Out)
                {
                    exps.Add($"{genParamDefVar}.IsCovariant = true;");
                }
                else if (typeParam.AllowsRefLikeType)
                {
                    context.EmitWarning("`allow ref struct` feature is not implemented yet.", typeParameterSyntax);
                }

                //https://github.com/adrianoc/cecilifier/issues/312
                // if (typeParam.HasNotNullConstraint)
                // {
                // }

                foreach (var type in typeParam.ConstraintTypes)
                {
                    exps.Add($"{genParamDefVar}.Constraints.Add(new GenericParameterConstraint({context.TypeResolver.ResolveAny(type)}));");
                }
            }
        }
        
        private static void ProcessGenericTypeParameters(string memberDefVar, IVisitorContext context, string ownerQualifiedTypeName, IList<string> typeParamList, IList<string> exps)
        {
            for (int i = 0; i < typeParamList.Count; i++)
            {
                var genericParamName = typeParamList[i];
                var genParamDefVar = context.Naming.SyntheticVariable(typeParamList[i], ElementKind.GenericParameter);
                exps.Add(GenericParameter(context, ownerQualifiedTypeName, memberDefVar, genericParamName, genParamDefVar));
                
                exps.Add($"{memberDefVar}.GenericParameters.Add({genParamDefVar});");
            }
        }

        public static IEnumerable<string> PropertyDefinition(string propDefVar, string propName, string propertyType)
        {
            return [$"var {propDefVar} = new PropertyDefinition(\"{propName}\", PropertyAttributes.None, {propertyType});"];
        }

        public static string DefaultTypeAttributeFor(TypeKind typeKind, bool hasStaticCtor)
        {
            var basicClassAttrs = "TypeAttributes.AnsiClass" + (hasStaticCtor ? "" : " | TypeAttributes.BeforeFieldInit");
            return typeKind switch
            {
                TypeKind.Struct => "TypeAttributes.Sealed |" + basicClassAttrs,
                TypeKind.Class => basicClassAttrs,
                TypeKind.Interface => "TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.BeforeFieldInit",
                TypeKind.Delegate => "TypeAttributes.Sealed",
                TypeKind.Enum => string.Empty,
                _ => throw new Exception("Not supported type declaration: " + typeKind)
            };
        }

        private static string FunctionPointerTypeBasedCecilType(ITypeResolver resolver, IFunctionPointerTypeSymbol functionPointer, Func<string, string, string, string> factory)
        {
            var parameters = $"Parameters={{ {string.Join(',', functionPointer.Signature.Parameters.Select(p => ParameterDoesNotHandleParamsKeywordOrDefaultValue(p.Name, p.RefKind, resolver.ResolveAny(p.Type))))} }}";
            var returnType = resolver.ResolveAny(functionPointer.Signature.ReturnType);
            return factory("HasThis = false", parameters, returnType);
        }

        public static void InstantiateDelegate(IVisitorContext context, string ilVar, ITypeSymbol delegateType, string targetMethodExp, StaticDelegateCacheContext staticDelegateCacheContext)
        {
            // To match Roslyn implementation we need to cache static method do delegate conversions.
            if (staticDelegateCacheContext.IsStaticDelegate)
            {
                staticDelegateCacheContext.EnsureCacheBackingFieldIsEmitted(context.TypeResolver.ResolveAny(delegateType));
                LogWarningIfStaticMethodIsDeclaredInOtherType(context, staticDelegateCacheContext);

                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldsfld, staticDelegateCacheContext.CacheBackingField);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);

                var cacheAlreadyInitializedTargetVarName = context.Naming.Label("cacheHit");
                context.Generate($"var {cacheAlreadyInitializedTargetVarName} = {ilVar}.Create(OpCodes.Nop);");
                context.WriteNewLine();
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Brtrue, cacheAlreadyInitializedTargetVarName);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Pop);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldnull);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldftn, targetMethodExp);
                var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor");
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(context));
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Stsfld, staticDelegateCacheContext.CacheBackingField);
                context.Generate($"{ilVar}.Append({cacheAlreadyInitializedTargetVarName});");
                context.WriteNewLine();
            }
            else
            {
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldftn, targetMethodExp);
                var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor");
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(context));
            }
        }

        private static void LogWarningIfStaticMethodIsDeclaredInOtherType(IVisitorContext context, StaticDelegateCacheContext staticDelegateCacheContext)
        {
            var currentType = context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
            if (currentType.IsValid && currentType.MemberName != staticDelegateCacheContext.Method.ContainingType.Name)
            {
                context.WriteComment($"*****************************************************************");
                context.WriteComment($"WARNING: Converting static method ({staticDelegateCacheContext.Method.FullyQualifiedName()}) to delegate in a type other than the one defining it may generate incorrect code. Access type: {currentType.MemberName}, Method type: {staticDelegateCacheContext.Method.ContainingType.Name}");
                context.WriteComment($"*****************************************************************");
            }
        }

        public static class Collections
        {
            /// <summary>
            /// When passing some types of params parameters Cecilifier needs to generate code to instantiate a List{T} and populate its values.
            /// In addition to that this method introduces a local variable of type <see cref="System.Span{T}"/> and initializes it with a
            /// reference to the instantiated <see cref="System.Collections.Generic.List{T}"/>.
            ///
            /// Callers can use that variable to initialize the list in a performant way.  
            /// </summary>
            /// <param name="context"></param>
            /// <param name="listOfTTypeSymbol"><see cref="ITypeSymbol"/> for the List{T}.</param>
            /// <param name="elementCount">Number of elements to be stored.</param>
            public static (DefinitionVariable, string) InstantiateListToStoreElements(IVisitorContext context, string ilVar, INamedTypeSymbol listOfTTypeSymbol, int elementCount)
            {
                var resolvedListTypeArgument = context.TypeResolver.ResolveAny(listOfTTypeSymbol.TypeArguments[0]);

                context.WriteNewLine();
                context.WriteComment("Instantiates a List<T> passing the # of elements to its ctor.");
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldc_I4, elementCount);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, listOfTTypeSymbol.Constructors.First(ctor => ctor.Parameters.Length == 1).MethodResolverExpression(context));

                // Pushes an extra copy of the reference to the list instance into the stack
                // to avoid introducing a local variable. This will be left at the top of the stack
                // when the initialization code finishes.
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);

                // Calls 'CollectionsMarshal.SetCount(list, num)' on the list.
                var collectionMarshalTypeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(System.Runtime.InteropServices.CollectionsMarshal).FullName!).EnsureNotNull();
                var setCountMethod = collectionMarshalTypeSymbol.GetMembers("SetCount").OfType<IMethodSymbol>().Single().MethodResolverExpression(context).MakeGenericInstanceMethod(context, "SetCount", [ resolvedListTypeArgument ]); 
                
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldc_I4, elementCount);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Call, setCountMethod);
                
                context.WriteNewLine();
                context.WriteComment("Add a Span<T> local variable and initialize it with `CollectionsMarshal.AsSpan(list)`");
                var spanToList = context.AddLocalVariableToCurrentMethod(
                    "listSpan", 
                    context.TypeResolver.ResolveAny(context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(resolvedListTypeArgument));

                context.ApiDriver.WriteCilInstruction(context, ilVar, 
                    OpCodes.Call, 
                    collectionMarshalTypeSymbol.GetMembers("AsSpan").OfType<IMethodSymbol>().Single().MethodResolverExpression(context).MakeGenericInstanceMethod(context, "AsSpan", [ resolvedListTypeArgument ]));
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Stloc, new CilLocalVariableHandle(spanToList.VariableName));
                
                return (spanToList, resolvedListTypeArgument);
            }
            
            public static string GetSpanIndexerGetter(IVisitorContext context, string typeArgument)
            {
                var methodVar = context.Naming.SyntheticVariable("getItem", ElementKind.Method);
                var declaringType = context.TypeResolver.ResolveAny(context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(typeArgument);
                context.Generate($$"""var {{methodVar}} = new MethodReference("get_Item", {{context.TypeResolver.Bcl.System.Void}}, {{declaringType}}) { HasThis = true, ExplicitThis = false };""");
                context.WriteNewLine();
                context.Generate($"{methodVar}.Parameters.Add(new ParameterDefinition({context.TypeResolver.Bcl.System.Int32}));");
                context.WriteNewLine();
                context.Generate($"""{methodVar}.ReturnType = ((GenericInstanceType) {methodVar}.DeclaringType).ElementType.GenericParameters[0].MakeByReferenceType();""");
                context.WriteNewLine();

                return methodVar;
            }
        }
    }
}
