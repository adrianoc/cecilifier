using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;

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

        string methodName = $"set_{property.Name}";
        string methodModifiers = property.AccessorModifiers["set"];
        IList<string> typeParameters = [];
        Func<IVisitorContext, string> returnTypeResolver = ctx => isInitOnly ? $"new RequiredModifierType({ctx.TypeResolver.Resolve(typeof(IsExternalInit).FullName)}, {ctx.TypeResolver.Bcl.System.Void})" : ctx.TypeResolver.Bcl.System.Void;
        var exps = Context.ApiDefinitionsFactory.Method(
                                            Context, 
                                            new MemberDefinitionContext(accessorMethodVar, property.DeclaringTypeVariable, IlContext.None), 
                                            property.DeclaringTypeNameForRegistration, 
                                            nameForRegistration, 
                                            methodName, 
                                            methodModifiers, 
                                            completeParamList, 
                                            typeParameters, 
                                            returnTypeResolver,
                                            out var methodDefinitionVariable);

        var methodVariableScope = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
        Context.Generate(exps);
        AddToOverridenMethodsIfAppropriated(accessorMethodVar, overridenMethod);

        Context.Generate([
                $"{accessorMethodVar}.Body = new MethodBody({accessorMethodVar});",
                $"{property.Variable}.SetMethod = {accessorMethodVar};" ]);
        
        return methodVariableScope;
    }

    internal void AddAutoSetterMethodImplementation(ref readonly PropertyGenerationData property, IlContext ilContext)
    {
        AddBackingFieldIfNeeded(in property);

        Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Ldarg_0);
        if (!property.IsStatic)
            Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Ldarg_1);

        var operand = property.DeclaringTypeIsGeneric ? MakeGenericInstanceType(in property) : _backingFieldVar;
        Context.ApiDriver.WriteCilInstruction(Context, ilContext, property.StoreOpCode, operand);
        Context.AddCompilerGeneratedAttributeTo(ilContext.RelatedMethodVariable);
    }

    internal ScopedDefinitionVariable AddGetterMethodDeclaration(ref readonly PropertyGenerationData property, string accessorMethodVar, bool hasCovariantReturn, string nameForRegistration, string overridenMethod)
    {
        var propertyResolvedType = property.ResolvedType;
        string methodName = $"get_{property.Name}";
        string methodModifiers = property.AccessorModifiers["get"];
        IList<string> typeParameters = [];
        Func<IVisitorContext, string> returnTypeResolver = _ => propertyResolvedType;
        var exps = Context.ApiDefinitionsFactory.Method(
            Context, 
            new MemberDefinitionContext(accessorMethodVar, property.DeclaringTypeVariable, IlContext.None), 
            property.DeclaringTypeNameForRegistration, 
            nameForRegistration, 
            methodName, 
            methodModifiers, 
            property.Parameters, 
            typeParameters, 
            returnTypeResolver,
            out var methodDefinitionVariable);
        
        Context.Generate(exps);
        
        var scopedVariable = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
        
        AddToOverridenMethodsIfAppropriated(accessorMethodVar, overridenMethod);
        
        Context.Generate([
            hasCovariantReturn ? 
                $"{accessorMethodVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(System.Runtime.CompilerServices.PreserveBaseOverridesAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));" 
                : String.Empty,
            $"{accessorMethodVar}.Body = new MethodBody({accessorMethodVar});",
            $"{property.Variable}.GetMethod = {accessorMethodVar};" ]);
        
        return scopedVariable;
    }
   
    internal void AddAutoGetterMethodImplementation(ref readonly PropertyGenerationData propertyGenerationData, string ilVar, string getMethodVar)
    {
        AddBackingFieldIfNeeded(in propertyGenerationData);

        if (!propertyGenerationData.IsStatic)
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
        
        Debug.Assert(_backingFieldVar != null);
        var operand = propertyGenerationData.DeclaringTypeIsGeneric ? MakeGenericInstanceType(in propertyGenerationData) : _backingFieldVar;
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, propertyGenerationData.LoadOpCode, operand);
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ret);
        
        Context.AddCompilerGeneratedAttributeTo(getMethodVar);
    }
    
    private void AddToOverridenMethodsIfAppropriated(string accessorMethodVar, string overridenMethod)
    {
        if (string.IsNullOrWhiteSpace(overridenMethod))
            return;
        
        Context.Generate($"{accessorMethodVar}.Overrides.Add({overridenMethod});");
        Context.WriteNewLine();
    }

    private void AddBackingFieldIfNeeded(ref readonly PropertyGenerationData property)
    {
        if (_backingFieldVar != null)
            return;

        _backingFieldVar = Context.Naming.SyntheticVariable(property.Name, ElementKind.Field);

        var name = Utils.BackingFieldNameForAutoProperty(property.Name);
        var backingFieldExps = Context.ApiDefinitionsFactory.Field(Context, new MemberDefinitionContext(_backingFieldVar, property.DeclaringTypeVariable, IlContext.None), property.DeclaringTypeNameForRegistration, name, property.ResolvedType, property.BackingFieldModifiers, false, false, null);
        
        Context.Generate(backingFieldExps);
        Context.AddCompilerGeneratedAttributeTo(_backingFieldVar);
    }
    
    private string MakeGenericInstanceType(ref readonly PropertyGenerationData property)
    {
        var genTypeVar = Context.Naming.SyntheticVariable(property.Name, ElementKind.GenericInstance);
        var fieldRefVar = Context.Naming.MemberReference("fld_");
        
        Context.Generate(
            [
                $"var {genTypeVar} = {property.DeclaringTypeVariable}.MakeGenericInstanceType({property.DeclaringTypeVariable}.GenericParameters.ToArray());",
                $"var {fieldRefVar} = new FieldReference({_backingFieldVar}.Name, {_backingFieldVar}.FieldType, {genTypeVar});"
            ]);
        return fieldRefVar;
    }
}
