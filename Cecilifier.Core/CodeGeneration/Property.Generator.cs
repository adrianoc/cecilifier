using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.CodeGeneration;

internal record struct PropertyGenerationData(
    string DeclaringTypeNameForRegistration,
    string DeclaringTypeVariable,
    bool DeclaringTypeIsGeneric,
    string Variable, 
    string Name, 
    IDictionary<string, string> AccessorModifiers,
    bool IsStatic,
    string ResolvedType, 
    string TypeNameForRegistration, 
    IReadOnlyList<ParameterSpec> Parameters,
    string BackingFieldModifiers =null, // Not used, unless on auto-properties
    OpCode StoreOpCode = default, // Not used, unless on auto-properties
    OpCode LoadOpCode = default); // Not used, unless on auto-properties

internal class PropertyGenerator
{
    private string _backingFieldVar;

    public PropertyGenerator(IVisitorContext context)
    {
        Context = context;
    }

    private IVisitorContext Context { get; init; }
    public string BackingFieldVariable => _backingFieldVar;

    internal ScopedDefinitionVariable AddSetterMethodDeclaration(ref readonly PropertyGenerationData property, string accessorMethodVar, bool isInitOnly, string nameForRegistration, string overridenMethod)
    {
        var completeParamList = new List<ParameterSpec>(property.Parameters)
        {
            // Setters always have at least one `value` parameter but Roslyn does not have it explicitly listed.
            new(
                "value",
                property.ResolvedType,
                RefKind.None,
                Constants.ParameterAttributes.None) { RegistrationTypeName = property.TypeNameForRegistration }
        };

        var exps = CecilDefinitionsFactory.Method(
                                            Context,
                                            property.DeclaringTypeNameForRegistration,
                                            accessorMethodVar,
                                            nameForRegistration,
                                            $"set_{property.Name}",
                                            property.AccessorModifiers["set"],
                                            completeParamList,
                                            [], // Properties cannot declare TypeParameters
                                            ctx => isInitOnly ? $"new RequiredModifierType({ctx.TypeResolver.Resolve(typeof(IsExternalInit).FullName)}, {ctx.TypeResolver.Bcl.System.Void})" : ctx.TypeResolver.Bcl.System.Void,
                                            out var methodDefinitionVariable);

        var methodVariableScope = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
        Context.WriteCecilExpressions(exps);
        AddToOverridenMethodsIfAppropriated(accessorMethodVar, overridenMethod);

        Context.WriteCecilExpressions([
                $"{property.DeclaringTypeVariable}.Methods.Add({accessorMethodVar});",
                $"{accessorMethodVar}.Body = new MethodBody({accessorMethodVar});",
                $"{property.Variable}.SetMethod = {accessorMethodVar};" ]);
        
        return methodVariableScope;
    }

    internal void AddAutoSetterMethodImplementation(ref readonly PropertyGenerationData property, string ilSetVar, string setMethodVar)
    {
        AddBackingFieldIfNeeded(in property);

        Context.EmitCilInstruction(ilSetVar, OpCodes.Ldarg_0);
        if (!property.IsStatic)
            Context.EmitCilInstruction(ilSetVar, OpCodes.Ldarg_1);

        var operand = property.DeclaringTypeIsGeneric ? MakeGenericType(in property) : _backingFieldVar;
        Context.EmitCilInstruction(ilSetVar, property.StoreOpCode, operand);
        AddCompilerGeneratedAttributeTo(Context, setMethodVar);
    }

    internal ScopedDefinitionVariable AddGetterMethodDeclaration(ref readonly PropertyGenerationData property, string accessorMethodVar, bool hasCovariantReturn, string nameForRegistration, string overridenMethod)
    {
        var propertyResolvedType = property.ResolvedType;
        var exps = CecilDefinitionsFactory.Method(
                                        Context,
                                        property.DeclaringTypeNameForRegistration,
                                        accessorMethodVar,
                                        nameForRegistration,
                                        $"get_{property.Name}",
                                        property.AccessorModifiers["get"],
                                        property.Parameters, 
                                        [], // Properties cannot declare TypeParameters
                                        _ => propertyResolvedType,
                                        out var methodDefinitionVariable);
        
        Context.WriteCecilExpressions(exps);
        
        var scopedVariable = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
        
        AddToOverridenMethodsIfAppropriated(accessorMethodVar, overridenMethod);
        
        Context.WriteCecilExpressions([
            hasCovariantReturn ? 
                $"{accessorMethodVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(System.Runtime.CompilerServices.PreserveBaseOverridesAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));" 
                : String.Empty,
            $"{property.DeclaringTypeVariable}.Methods.Add({accessorMethodVar});",
            $"{accessorMethodVar}.Body = new MethodBody({accessorMethodVar});",
            $"{property.Variable}.GetMethod = {accessorMethodVar};" ]);
        
        return scopedVariable;
    }
   
    internal void AddAutoGetterMethodImplementation(ref readonly PropertyGenerationData propertyGenerationData, string ilVar, string getMethodVar)
    {
        AddBackingFieldIfNeeded(in propertyGenerationData);

        if (!propertyGenerationData.IsStatic)
            Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
        
        Debug.Assert(_backingFieldVar != null);
        var operand = propertyGenerationData.DeclaringTypeIsGeneric ? MakeGenericType(in propertyGenerationData) : _backingFieldVar;
        Context.EmitCilInstruction(ilVar, propertyGenerationData.LoadOpCode, operand);
        Context.EmitCilInstruction(ilVar, OpCodes.Ret);
        
        AddCompilerGeneratedAttributeTo(Context, getMethodVar);
    }
    
    private void AddToOverridenMethodsIfAppropriated(string accessorMethodVar, string overridenMethod)
    {
        if (string.IsNullOrWhiteSpace(overridenMethod))
            return;
        
        Context.WriteCecilExpression($"{accessorMethodVar}.Overrides.Add({overridenMethod});");
        Context.WriteNewLine();
    }

    private void AddBackingFieldIfNeeded(ref readonly PropertyGenerationData property)
    {
        if (_backingFieldVar != null)
            return;

        _backingFieldVar = Context.Naming.SyntheticVariable(property.Name, ElementKind.Field);
        
        var backingFieldExps = CecilDefinitionsFactory.Field(
            Context,
            property.DeclaringTypeNameForRegistration,
            property.DeclaringTypeVariable,
            _backingFieldVar,
            Utils.BackingFieldNameForAutoProperty(property.Name),
            property.ResolvedType,
            property.BackingFieldModifiers);
        
        Context.WriteCecilExpressions(backingFieldExps);
        AddCompilerGeneratedAttributeTo(Context, _backingFieldVar);
    }
 
    private void AddCompilerGeneratedAttributeTo(IVisitorContext context, string memberVariable)
    {
        var compilerGeneratedAttributeCtor = context.RoslynTypeSystem.SystemRuntimeCompilerServicesCompilerGeneratedAttribute.Ctor();
        var exps = CecilDefinitionsFactory.Attribute("compilerGenerated", memberVariable, context, compilerGeneratedAttributeCtor.MethodResolverExpression(context));
        context.WriteCecilExpressions(exps);
    }

    private string MakeGenericType(ref readonly PropertyGenerationData property)
    {
        var genTypeVar = Context.Naming.SyntheticVariable(property.Name, ElementKind.GenericInstance);
        var fieldRefVar = Context.Naming.MemberReference("fld_");
        
        Context.WriteCecilExpressions(
            [
                $"var {genTypeVar} = {property.DeclaringTypeVariable}.MakeGenericInstanceType({property.DeclaringTypeVariable}.GenericParameters.ToArray());",
                $"var {fieldRefVar} = new FieldReference({_backingFieldVar}.Name, {_backingFieldVar}.FieldType, {genTypeVar});"
            ]);
        return fieldRefVar;
    }
}
