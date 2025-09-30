using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
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

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, MemberDefinitionContext memberDefinitionContext, string methodName, string methodModifiers,
        IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters)
    {
        var exps = CecilDefinitionsFactory.Method(context, memberDefinitionContext.MemberDefinitionVariableName, methodName, methodModifiers, methodSymbol.ReturnType, methodSymbol.ReturnsByRef || methodSymbol.ReturnsByRef, typeParameters).ToList();
        exps.Add($"{context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName}.Methods.Add({memberDefinitionContext.MemberDefinitionVariableName});");
        if (methodSymbol is { IsAbstract: false, IsExtern: false })
        {
            exps.Add($"{memberDefinitionContext.MemberDefinitionVariableName}.Body.InitLocals = {(!methodSymbol.TryGetAttribute<SkipLocalsInitAttribute>(out _)).ToString().ToLower()};");
        }
        return exps;
    }

    public IEnumerable<string> Method(IVisitorContext context,
        MemberDefinitionContext memberDefinitionContext,
        string declaringTypeName,
        string methodNameForVariableRegistration,
        string methodName,
        string methodModifiers,
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        ITypeSymbol returnType)
    {
        var exps = CecilDefinitionsFactory.Method(
                            context, 
                            declaringTypeName, 
                            memberDefinitionContext.MemberDefinitionVariableName, 
                            methodNameForVariableRegistration, 
                            methodName, 
                            methodModifiers, 
                            parameters, 
                            typeParameters, 
                            ctx => ctx.TypeResolver.ResolveAny(returnType),
                            out _);
        
        exps =  [
            ..exps, 
            $"{memberDefinitionContext.ParentDefinitionVariableName}.Methods.Add({memberDefinitionContext.MemberDefinitionVariableName});",
        ];
        
        return exps;
    }

    public IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, string[] localVariableTypes, InstructionRepresentation[] instructions)
    {
        var tagToInstructionDefMapping = new Dictionary<string, string>();
        yield return $"{ilContext.RelatedMethodVariable}.Body = new MethodBody({ilContext.RelatedMethodVariable});"; 
        yield return $"{ilContext.RelatedMethodVariable}.Body.InitLocals = true;";

        if (localVariableTypes.Length > 0)
        {
            foreach (var localVariableType in localVariableTypes)
            {
                yield return $"{ilContext.RelatedMethodVariable}.Body.Variables.Add({LocalVariable(localVariableType)});";
            }
        }

        if (instructions.Length == 0)
            yield break;

        var methodInstVar = context.Naming.SyntheticVariable(methodName, ElementKind.LocalVariable);
        yield return $"var {methodInstVar} = {ilContext.RelatedMethodVariable}.Body.Instructions;";

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

    public IEnumerable<string> Constructor(IVisitorContext context, MemberDefinitionContext memberDefinitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null)
    {
        var ctorName = Utils.ConstructorMethodName(isStatic);
        context.DefinitionVariables.RegisterMethod(typeName, ctorName, paramTypes, 0, memberDefinitionContext.MemberDefinitionVariableName);

        var exp = $@"var {memberDefinitionContext.MemberDefinitionVariableName} = new MethodDefinition(""{ctorName}"", {methodAccessibility} | MethodAttributes.HideBySig | {Constants.Cecil.CtorAttributes}, assembly.MainModule.TypeSystem.Void)";
        if (methodDefinitionPropertyValues != null)
        {
            exp = exp + $"{{ {methodDefinitionPropertyValues} }}";
        }

        return [exp + ";", $"{memberDefinitionContext.ParentDefinitionVariableName}.Methods.Add({memberDefinitionContext.MemberDefinitionVariableName});"];
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        return Field(context, memberDefinitionContext, fieldOrEvent.ContainingType.ToDisplayString(), fieldOrEvent.Name, context.TypeResolver.ResolveAny(fieldType), fieldAttributes, isVolatile, isByRef, constantValue);
    }

    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, string declaringTypeName, string name, string fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null)
    {
        if (isByRef)
            fieldType = fieldType.MakeByReferenceType();
        
        context.DefinitionVariables.RegisterNonMethod(declaringTypeName, name, VariableMemberKind.Field, memberDefinitionContext.MemberDefinitionVariableName);
        
        var resolvedFieldType = ProcessRequiredModifiers(context, fieldType, isVolatile);
        var fieldExp = $"var {memberDefinitionContext.MemberDefinitionVariableName} = new FieldDefinition(\"{name}\", {fieldAttributes}, {resolvedFieldType})";
        List<string> exps = 
        [
            constantValue != null ? $"{fieldExp} {{ Constant = {constantValue} }} ;" : $"{fieldExp};",
            $"{memberDefinitionContext.ParentDefinitionVariableName}.Fields.Add({memberDefinitionContext.MemberDefinitionVariableName});"
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
    
    private static string GenericParameter(IVisitorContext context, string typeParameterOwnerVar, string genericParamName, string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)
    {
        context.DefinitionVariables.RegisterNonMethod(typeParameterSymbol.ContainingSymbol.ToDisplayString(), genericParamName, VariableMemberKind.TypeParameter, genParamDefVar);
        return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeParameterOwnerVar}){Variance(typeParameterSymbol)};";
    }

    private static string Variance(ITypeParameterSymbol typeParameterSymbol)
    {
        return typeParameterSymbol.Variance switch
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
