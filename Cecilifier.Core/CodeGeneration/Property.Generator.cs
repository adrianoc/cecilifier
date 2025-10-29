using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
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
    Func<ResolveTargetKind, string> Type, 
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

    internal ScopedDefinitionVariable AddSetterMethodDeclaration(ref readonly PropertyGenerationData property, string accessorMethodVar, bool isInitOnly, string nameForRegistration, string overridenMethod, in IlContext ilContext)
    {
        var completeParamList = new List<ParameterSpec>(property.Parameters)
        {
            // Setters always have at least one `value` parameter but Roslyn does not have it explicitly listed.
            new(
                "value",
                property.Type(ResolveTargetKind.ReturnType),
                RefKind.None,
                Constants.ParameterAttributes.None) { RegistrationTypeName = property.TypeNameForRegistration }
        };

        IList<string> typeParameters = [];
        var memberOptions = (property.IsStatic ? MemberOptions.Static :  MemberOptions.None) 
                            | (isInitOnly ? MemberOptions.InitOnly : MemberOptions.None);
        
        var exps = Context.ApiDefinitionsFactory.Method(
                                            Context, 
                                            new BodiedMemberDefinitionContext($"set_{property.Name}", accessorMethodVar, property.DeclaringTypeVariable, memberOptions, ilContext), 
                                            property.DeclaringTypeNameForRegistration, 
                                            property.AccessorModifiers["set"], 
                                            completeParamList, 
                                            typeParameters,
                                            ctx => ctx.TypeResolver.ResolveAny(Context.RoslynTypeSystem.SystemVoid, ResolveTargetKind.ReturnType),
                                            out var methodDefinitionVariable);

        var methodVariableScope = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
        Context.Generate(exps);
        AddToOverridenMethodsIfAppropriated(accessorMethodVar, overridenMethod);

        Context.ApiDriver.AddMethodSemantics(Context, property.Variable, accessorMethodVar, MethodKind.PropertySet);
        
        return methodVariableScope;
    }

    internal void AddAutoSetterMethodImplementation(ref readonly PropertyGenerationData property, IlContext ilContext)
    {
        AddBackingFieldIfNeeded(in property);

        Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Ldarg_0);
        if (!property.IsStatic)
            Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Ldarg_1);

        var operand = property.DeclaringTypeIsGeneric ? MakeGenericInstanceType(in property) : _backingFieldVar;
        Context.ApiDriver.WriteCilInstruction(Context, ilContext, property.StoreOpCode, operand.AsToken());
        Context.AddCompilerGeneratedAttributeTo(ilContext.AssociatedMethodVariable, VariableMemberKind.Method);
    }

    internal ScopedDefinitionVariable AddGetterMethodDeclaration(ref readonly PropertyGenerationData property, string accessorMethodVar, bool hasCovariantReturn, string overridenMethod, in IlContext ilContext)
    {
        var propertyResolvedType = property.Type(ResolveTargetKind.ReturnType);
        IList<string> typeParameters = [];
        var memberDefinitionContext = new BodiedMemberDefinitionContext($"get_{property.Name}", accessorMethodVar, property.DeclaringTypeVariable, property.IsStatic ? MemberOptions.Static : MemberOptions.None, ilContext);
        var exps = Context.ApiDefinitionsFactory.Method(
                                                                    Context, 
                                                                    memberDefinitionContext, 
                                                                    property.DeclaringTypeNameForRegistration, 
                                                                    property.AccessorModifiers["get"], 
                                                                    property.Parameters, 
                                                                    typeParameters, 
                                                                    ctx => propertyResolvedType,
                                                                    out var methodDefinitionVariable);
        
        Context.Generate(exps);
        
        var scopedVariable = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
        
        AddToOverridenMethodsIfAppropriated(accessorMethodVar, overridenMethod);
        
        Context.Generate([
            hasCovariantReturn ? 
                $"{accessorMethodVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(System.Runtime.CompilerServices.PreserveBaseOverridesAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));" 
                : String.Empty]);
            
        Context.ApiDriver.AddMethodSemantics(Context, property.Variable, accessorMethodVar, MethodKind.PropertyGet);
        return scopedVariable;
    }
   
    internal void AddAutoGetterMethodImplementation(ref readonly PropertyGenerationData propertyGenerationData, string ilVar, string getMethodVar)
    {
        AddBackingFieldIfNeeded(in propertyGenerationData);

        if (!propertyGenerationData.IsStatic)
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
        
        Debug.Assert(_backingFieldVar != null);
        var operand = propertyGenerationData.DeclaringTypeIsGeneric ? MakeGenericInstanceType(in propertyGenerationData) : _backingFieldVar;
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, propertyGenerationData.LoadOpCode, operand.AsToken());
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ret);
        
        Context.AddCompilerGeneratedAttributeTo(getMethodVar, VariableMemberKind.Method);
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
        var memberDefinitionContext = new MemberDefinitionContext(name, $"{property.Name}BackingField", _backingFieldVar, property.DeclaringTypeVariable);
        var backingFieldExps = Context.ApiDefinitionsFactory.Field(Context, memberDefinitionContext, property.DeclaringTypeNameForRegistration, name, property.Type(ResolveTargetKind.Field), property.BackingFieldModifiers, false, false, null);
        
        Context.Generate(backingFieldExps);
        Context.AddCompilerGeneratedAttributeTo(_backingFieldVar, VariableMemberKind.Field);
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
