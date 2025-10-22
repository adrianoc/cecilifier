using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;

namespace Cecilifier.ApiDriver.MonoCecil;

internal class MonoCecilDefinitionsFactory : DefinitionsFactoryBase, IApiDriverDefinitionsFactory
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
        var typeParamList = ownTypeParameters?.ToArray() ?? [];
        if (typeParamList.Length > 0)
        {
            typeName = typeName + "`" + typeParamList.Length;
        }

        var exps = new List<string>();
        var typeDefExp = $"var {typeVar} = new TypeDefinition(\"{typeNamespace}\", \"{typeName}\", {attrs}{(!string.IsNullOrWhiteSpace(resolvedBaseType) ? ", " + resolvedBaseType : "")})";
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

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, BodiedMemberDefinitionContext bodiedMemberDefinitionContext, string methodName, string methodModifiers,
        IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters)
    {
        var exps = new List<string>();

        var resolvedReturnType = context.TypeResolver.ResolveAny(methodSymbol.ReturnType);
        var refReturn = methodSymbol.ReturnsByRef || methodSymbol.ReturnsByRefReadonly;
        if (refReturn)
            resolvedReturnType = resolvedReturnType.MakeByReferenceType();

        // for type parameters we may need to postpone setting the return type (using void as a placeholder, since we need to pass something) until the generic parameters has been
        // handled. This is required because the type parameter may be defined by the method being processed.
        exps.Add($"var {bodiedMemberDefinitionContext.Member.DefinitionVariable} = new MethodDefinition(\"{methodName}\", {methodModifiers}, {(methodSymbol.ReturnType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter() ? context.TypeResolver.Bcl.System.Void : resolvedReturnType)});");
        ProcessGenericTypeParameters(bodiedMemberDefinitionContext.Member.DefinitionVariable, context, typeParameters, exps);
        if (methodSymbol.ReturnType.IsTypeParameterOrIsGenericTypeReferencingTypeParameter())
        {
            resolvedReturnType = context.TypeResolver.ResolveAny(methodSymbol.ReturnType);
            exps.Add($"{bodiedMemberDefinitionContext.Member.DefinitionVariable}.ReturnType = {(refReturn ? resolvedReturnType.MakeByReferenceType() : resolvedReturnType)};");
        }

        exps.Add($"{context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName}.Methods.Add({bodiedMemberDefinitionContext.Member.DefinitionVariable});");
        if (methodSymbol is { IsAbstract: false, IsExtern: false })
        {
            exps.Add($"{bodiedMemberDefinitionContext.Member.DefinitionVariable}.Body.InitLocals = {(!methodSymbol.TryGetAttribute<SkipLocalsInitAttribute>(out _)).ToString().ToLower()};");
        }
        return exps;
    }

    public IEnumerable<string> Method(IVisitorContext context,
        BodiedMemberDefinitionContext definitionContext,
        string declaringTypeName,
        string methodModifiers,
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        Func<IVisitorContext, string> returnTypeResolver,
        out MethodDefinitionVariable methodVariable)
    {
        var exps = new List<string>();

        Func<IVisitorContext, string> f = returnTypeResolver; 
        if ((definitionContext.Options & MemberOptions.InitOnly) == MemberOptions.InitOnly)
        {
            returnTypeResolver = ctx => $"new RequiredModifierType({context.TypeResolver.Resolve(ctx.RoslynTypeSystem.ForType(typeof(IsExternalInit).FullName))}, {f(ctx)})";
        }
        
        // if the method has type parameters we need to postpone setting the return type (using void as a placeholder, since we need to pass something) until the generic parameters has been
        // handled. This is required because the type parameter may be defined by the method being processed which introduces a chicken and egg problem.
        exps.Add($"var {definitionContext.Member.DefinitionVariable} = new MethodDefinition(\"{definitionContext.Member.Name}\", {methodModifiers}, {(typeParameters.Count == 0 ? returnTypeResolver(context) : context.TypeResolver.Bcl.System.Void)});");
        ProcessGenericTypeParameters(definitionContext.Member.DefinitionVariable, context, definitionContext.Member.Identifier, typeParameters, exps);
        if (typeParameters.Count > 0)
            exps.Add($"{definitionContext.Member.DefinitionVariable}.ReturnType = {returnTypeResolver(context)};");

        foreach (var parameter in parameters)
        {
            var paramVar = context.Naming.SyntheticVariable(parameter.Name, ElementKind.Parameter);
            var parameterExp = CecilDefinitionsFactory.Parameter(
                                                                            parameter.Name,
                                                                            parameter.RefKind,
                                                                            parameter.ParamsAttributeName, // for now,the only callers for this method don't have any `params` parameters.
                                                                            definitionContext.Member.DefinitionVariable,
                                                                            paramVar,
                                                                            parameter.ElementTypeResolver != null ? parameter.ElementTypeResolver(context, parameter.ElementType) : parameter.ElementType,
                                                                            parameter.Attributes,
                                                                            (parameter.DefaultValue, parameter.DefaultValue != null));

            context.DefinitionVariables.RegisterNonMethod(definitionContext.Member.Identifier, parameter.Name, VariableMemberKind.Parameter, paramVar);
            exps.AddRange(parameterExp);
        }

        if (definitionContext.Member.ParentDefinitionVariable != null)
        {
            methodVariable = context.DefinitionVariables.RegisterMethod(declaringTypeName, definitionContext.Member.Name, parameters.Select(p => p.RegistrationTypeName).ToArray(), typeParameters.Count, definitionContext.Member.DefinitionVariable);
            exps =
            [
                ..exps,
                $"{definitionContext.Member.ParentDefinitionVariable}.Methods.Add({definitionContext.Member.DefinitionVariable});",
            ];
        }
        else
            methodVariable = MethodDefinitionVariable.MethodNotFound;

        return exps;
    }

    public IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, string[] localVariableTypes, InstructionRepresentation[] instructions)
    {
        var tagToInstructionDefMapping = new Dictionary<string, string>();
        yield return $"{ilContext.AssociatedMethodVariable}.Body = new MethodBody({ilContext.AssociatedMethodVariable});"; 
        yield return $"{ilContext.AssociatedMethodVariable}.Body.InitLocals = true;";

        if (localVariableTypes.Length > 0)
        {
            foreach (var localVariableType in localVariableTypes)
            {
                yield return $"{ilContext.AssociatedMethodVariable}.Body.Variables.Add({LocalVariable(localVariableType)});";
            }
        }

        if (instructions.Length == 0)
            yield break;

        var methodInstVar = context.Naming.SyntheticVariable(methodName, ElementKind.LocalVariable);
        yield return $"var {methodInstVar} = {ilContext.AssociatedMethodVariable}.Body.Instructions;";

        // create `Mono.Cecil.Instruction` instances for each instruction that has a 'Tag'
        foreach (var inst in instructions.Where(inst => !inst.Ignore))
        {
            if (inst.Tag == null)
                continue;

            var instVar = context.Naming.SyntheticVariable(inst.Tag, ElementKind.Label);
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

    public IEnumerable<string> Constructor(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null)
    {
        var ctorName = Utils.ConstructorMethodName(isStatic);
        context.DefinitionVariables.RegisterMethod(typeName, ctorName, paramTypes, 0, definitionContext.Member.DefinitionVariable);

        var exp = $@"var {definitionContext.Member.DefinitionVariable} = new MethodDefinition(""{ctorName}"", {methodAccessibility} | MethodAttributes.HideBySig | {Constants.Cecil.CtorAttributes}, assembly.MainModule.TypeSystem.Void)";
        if (methodDefinitionPropertyValues != null)
        {
            exp = exp + $"{{ {methodDefinitionPropertyValues} }}";
        }

        return [exp + ";", $"{definitionContext.Member.ParentDefinitionVariable}.Methods.Add({definitionContext.Member.DefinitionVariable});"];
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        return Field(context, definitionContext, fieldOrEvent.ContainingType.ToDisplayString(), fieldOrEvent.Name, context.TypeResolver.ResolveAny(fieldType), fieldAttributes, isVolatile, isByRef, constantValue);
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, string declaringTypeName, string name, string fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        if (isByRef)
            fieldType = fieldType.MakeByReferenceType();
        
        context.DefinitionVariables.RegisterNonMethod(declaringTypeName, name, VariableMemberKind.Field, definitionContext.DefinitionVariable);
        
        var resolvedFieldType = ProcessRequiredModifiers(context, fieldType, isVolatile);
        var fieldExp = $"var {definitionContext.DefinitionVariable} = new FieldDefinition(\"{name}\", {fieldAttributes}, {resolvedFieldType})";
        List<string> exps = 
        [
            constantValue != null ? $"{fieldExp} {{ Constant = {constantValue} }} ;" : $"{fieldExp};",
            $"{definitionContext.ParentDefinitionVariable}.Fields.Add({definitionContext.DefinitionVariable});"
        ];

        return exps;
    }

    public DefinitionVariable LocalVariable(IVisitorContext context, string variableName, string methodDefinitionVariableName, string resolvedVarType)
    {
        var cecilVarDeclName = context.Naming.SyntheticVariable(variableName, ElementKind.LocalVariable);

        context.Generate($"var {cecilVarDeclName} = new VariableDefinition({resolvedVarType});");
        context.WriteNewLine();
        context.Generate($"{methodDefinitionVariableName}.Body.Variables.Add({cecilVarDeclName});");
        context.WriteNewLine();

        return context.DefinitionVariables.RegisterNonMethod(string.Empty, variableName, VariableMemberKind.LocalVariable, cecilVarDeclName);
    }

    public IEnumerable<string> Property(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string declaringTypeName, List<ParameterSpec> propertyParameters, string propertyType)
    {
        return [
            $"var {definitionContext.Member.DefinitionVariable} = new PropertyDefinition(\"{definitionContext.Member.Name}\", PropertyAttributes.None, {propertyType});",
            $"{definitionContext.Member.ParentDefinitionVariable}.Properties.Add({definitionContext.Member.DefinitionVariable});"
        ];
    }
    
    public IEnumerable<string> Attribute(IVisitorContext context, IMethodSymbol attributeCtor, string attributeVarBaseName, string attributeTargetVar,  VariableMemberKind targetKind, params CustomAttributeArgument[] arguments)
    {
        var attributeVar = context.Naming.SyntheticVariable(attributeVarBaseName, ElementKind.Attribute);
        var resolvedCtor = context.MemberResolver.ResolveMethod(attributeCtor);
        
        //TODO: To be on par with original implementation of CecilDefinitionsFactory.Attribute() we only process positional arguments (ignoring named ones)
        //      There is another overload of Attribute() method that does handle all arguments; most likely we'll merge the 2 overloads; when that happen
        //      we'll need to revisit this code.
        var namedArguments = arguments.OfType<CustomAttributeNamedArgument>();
        var positionalArguments = arguments.Except(namedArguments).ToArray();
        
        var exps = new string[2 + positionalArguments.Length];
        int expIndex = 0;
        exps[expIndex++] = $"var {attributeVar} = new CustomAttribute({resolvedCtor});";

        for(int i = 0; i < positionalArguments.Length; i++)
        {
            var attributeArgument = $"new CustomAttributeArgument({context.TypeResolver.Resolve(attributeCtor.Parameters[i].Type)}, {positionalArguments[i].Value})";
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
            //TODO: Accept `variance` as a parameter. 
            exps.Add(GenericParameter(context, ownerQualifiedTypeName, memberDefVar, genericParamName, genParamDefVar, VarianceKind.None));
                
            exps.Add($"{memberDefVar}.GenericParameters.Add({genParamDefVar});");
        }
    }
    
    private static string GenericParameter(IVisitorContext context, string typeParameterOwnerVar, string genericParamName, string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)
    {
        return GenericParameter(context, typeParameterSymbol.ContainingSymbol.ToDisplayString(), typeParameterOwnerVar, genericParamName, genParamDefVar, typeParameterSymbol.Variance);
    }
    
    private static string GenericParameter(IVisitorContext context, string ownerContainingTypeName, string typeParameterOwnerVar, string genericParamName, string genParamDefVar, VarianceKind variance)
    {
        context.DefinitionVariables.RegisterNonMethod(ownerContainingTypeName, genericParamName, VariableMemberKind.TypeParameter, genParamDefVar);
        return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeParameterOwnerVar}){Variance(variance)};";
    }

    private static string Variance(ITypeParameterSymbol typeParameterSymbol) => Variance(typeParameterSymbol.Variance);
    
    private static string Variance(VarianceKind variance)
    {
        return variance switch
        {
            VarianceKind.In => " { IsContravariant = true }",
            VarianceKind.Out => " { IsCovariant = true }",
            _ => string.Empty
        };
    }

    private static string LocalVariable(string resolvedType) => $"new VariableDefinition({resolvedType})";
    
    private string ProcessRequiredModifiers(IVisitorContext context, string originalType, bool isVolatile)
    {
        if (!isVolatile)
            return originalType;
        
        var id = context.Naming.RequiredModifier();
        context.Generate($"var {id} = new RequiredModifierType({context.TypeResolver.Resolve(typeof(IsVolatile).FullName)}, {originalType});");
        
        return id;
    }
}
