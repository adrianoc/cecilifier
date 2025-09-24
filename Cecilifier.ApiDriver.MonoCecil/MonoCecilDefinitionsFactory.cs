using System.Runtime.CompilerServices;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        return CecilDefinitionsFactory.Type(context, typeVar, typeNamespace, typeName, attrs, resolvedBaseType, outerTypeVariable, isStructWithNoFields, interfaces, ownTypeParameters, outerTypeParameters, properties);
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
