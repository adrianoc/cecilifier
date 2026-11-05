using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.AST;
using Cecilifier.Core.CodeGeneration.Extensions;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.CodeGeneration;

internal partial class RecordGenerator
{
    private readonly IVisitorContext context;
    private readonly string recordTypeDefinitionVariable;
    private readonly RecordDeclarationSyntax record;
    private string _equalityContractGetMethodVar;
    private string _recordTypeEqualsOverloadMethodVar;
    private IDictionary<string, (string GetDefaultMethodVar, string EqualsMethodVar, string GetHashCodeMethodVar)> _equalityComparerMembersCache;
    private INamedTypeSymbol _recordSymbol;
    private Action<IVisitorContext, ITypeSymbol, string> _isReadOnlyAttributeHandler;

    public RecordGenerator(IVisitorContext context, string recordTypeDefinitionVariable, RecordDeclarationSyntax record)
    {
        this.context = context;
        this.recordTypeDefinitionVariable = recordTypeDefinitionVariable;
        this.record = record;
    }

    public void AddNullabilityAttributesToTypeDefinition(string definitionVar)
    {
        var nullableAttributeCtor = context.RoslynTypeSystem.ForType<NullableAttribute>().Ctor(context.RoslynTypeSystem.SystemByte);
        var nullableAttrExps = context.ApiDefinitionsFactory.Attribute(context, nullableAttributeCtor, "nullable", definitionVar, VariableMemberKind.Type, new CustomAttributeArgument { Value = (byte) NullableAwareness.NullableOblivious }); 
        context.Generate(nullableAttrExps);
        AddNullableContextAttributeTo(definitionVar, NullableAwareness.NonNullable);
    }
    
    internal void AddSyntheticMembers()
    {
        _recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, INamedTypeSymbol>();

        InitializeIsReadOnlyAttributeHandler(_recordSymbol);
        InitializeEqualityComparerMemberCache();
        
        AddEqualityContractPropertyIfNeeded();
        AddPropertiesFrom();
        PrimaryConstructorGenerator.AddPrimaryConstructor(context, recordTypeDefinitionVariable, record);
        AddCopyConstructor();
        AddCloneMethod();
        AddIEquatableEquals();
        AddToStringAndRelatedMethods();
        AddGetHashCodeMethod();
        AddEqualsOverloads();
        var equalityOperatorMethod = AddEqualityOperator();
        AddInequalityOperator(equalityOperatorMethod);
        AddDeconstructMethod();
    }

    private void AddCloneMethod()
    {
        if (_recordSymbol.IsValueType || record.ParameterList?.Parameters.Count == 0)
            return;
        
        const string CloneMethodName = "<Clone>$";
        context.WriteComment($"{_recordSymbol.Name} {CloneMethodName} method");

        var cloneMethodVar = context.Naming.SyntheticVariable("clone", ElementKind.Method);
        var ilContext = context.ApiDriver.NewIlContext(context, "clone", cloneMethodVar);
        var clonedMethodExps = context.ApiDefinitionsFactory.Method(
                                                                                context, 
                                                                                new BodiedMemberDefinitionContext( CloneMethodName, "Clone", cloneMethodVar, recordTypeDefinitionVariable, MemberOptions.None, ilContext),  
                                                                                recordTypeDefinitionVariable,
                                                                                Constants.Cecil.HideBySigNewSlotVirtual.AppendEnumFlag("MethodAttributes.Public"), 
                                                                                [], 
                                                                                [], 
                                                                                ctx => ctx.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.ReturnType), 
                                                                                out var _);
        context.Generate(clonedMethodExps);

        var copyCtorVarToFind = _recordSymbol.GetMembers(".ctor").OfType<IMethodSymbol>().Single(c => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, c.ContainingType)).AsMethodDefinitionVariable();
        var copyCtorVar = context.DefinitionVariables.GetMethodVariable(copyCtorVarToFind);
        if (!copyCtorVar.IsValid)
        {
            throw new Exception($"Copy constructor definition variable for record {_recordSymbol.Name} could not be found.");
        }

        InstructionRepresentation[] instructions = 
        [
            OpCodes.Ldarg_0,
            OpCodes.Newobj.WithOperand(MethodOnClosedGenericTypeForMethodVariable(copyCtorVar.VariableName, recordTypeDefinitionVariable, context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.Instruction)).AsToken()),
            OpCodes.Ret
        ];

        context.Generate(context.ApiDefinitionsFactory.MethodBody(context, "clone", ilContext, [], instructions));
        AddCompilerGeneratedAttributeTo(context, cloneMethodVar, VariableMemberKind.Method);
    }

    private void AddCopyConstructor()
    {
        if (_recordSymbol.IsValueType || record.ParameterList?.Parameters.Count == 0)
            return;
        
        context.WriteComment($"{_recordSymbol.Name} copy constructor");
        var copyCtorVarToFind = _recordSymbol.GetMembers(".ctor").OfType<IMethodSymbol>().Single(c => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, c.ContainingType)).AsMethodDefinitionVariable();
        var found = context.DefinitionVariables.GetMethodVariable(copyCtorVarToFind);
        var copyCtorVar = found.VariableName;

        if (!found.IsValid)
        {
            copyCtorVar = context.Naming.Constructor(record, false);
            var exps = context.ApiDefinitionsFactory.Constructor(
                                                            context, 
                                                            new BodiedMemberDefinitionContext("ctor", copyCtorVar, recordTypeDefinitionVariable, MemberOptions.None, IlContext.None), 
                                                            _recordSymbol.OriginalDefinition.ToDisplayString(), 
                                                            false, 
                                                            Constants.Cecil.CtorAttributes.AppendEnumFlag("MethodAttributes.Family | MethodAttributes.HideBySig"),
                                                            [
                                                                new ParameterSpec("other", context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = _recordSymbol.ToDisplayString() }
                                                            ]);

            context.Generate(exps);
        }
        else
            context.Generate($"{recordTypeDefinitionVariable}.Methods.Add({copyCtorVar});");
        
        context.WriteNewLine();

        List<InstructionRepresentation> instructions = new();
        if (HasBaseRecord(context, _recordSymbol))
        {
            instructions.Add(OpCodes.Ldarg_0);
            instructions.Add(OpCodes.Ldarg_1);
            var tbf = _recordSymbol.BaseType!.GetMembers(".ctor").OfType<IMethodSymbol>().Single(c => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, c.ContainingType)).AsMethodDefinitionVariable();
            var baseCopyCtorVar = context.DefinitionVariables.GetMethodVariable(tbf);
            instructions.Add(OpCodes.Call.WithOperand(baseCopyCtorVar.VariableName.AsToken()));
        }
        else
        {
            var baseCtor = context.RoslynTypeSystem.SystemObject.GetMembers(".ctor").OfType<IMethodSymbol>().Single(c => c.Parameters.Length == 0).MethodResolverExpression(context);
            instructions.Add(OpCodes.Ldarg_0);
            instructions.Add(OpCodes.Call.WithOperand(baseCtor.AsToken()));
        }
        
        foreach (var parameter in record.GetUniqueParameters(context))
        {
            var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, _recordSymbol.OriginalDefinition.ToDisplayString());
            var backingFieldRef = _recordSymbol is INamedTypeSymbol { IsGenericType: true } 
                    ? $"new FieldReference({paramDefVar.VariableName}.Name, {paramDefVar.VariableName}.FieldType, {context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.Field)})" 
                    : paramDefVar.VariableName;
                
            instructions.Add(OpCodes.Ldarg_0);
            
            // load property backing field for 'other' 
            instructions.Add(OpCodes.Ldarg_1);
            instructions.Add(OpCodes.Ldfld.WithOperand(backingFieldRef.AsToken()));
                
            // stores to property backing field on 'this' 
            instructions.Add(OpCodes.Stfld.WithOperand(backingFieldRef.AsToken()));
        }
        instructions.Add(OpCodes.Ret);
        
        context.Generate(
            context.ApiDefinitionsFactory.MethodBody(context, Constants.Cecil.InstanceConstructorName, context.ApiDriver.NewIlContext(context, Constants.Cecil.InstanceConstructorName, copyCtorVar), [], instructions.ToArray())
        );
        
        AddCompilerGeneratedAttributeTo(context, copyCtorVar, VariableMemberKind.Method);
    }

    private void AddPropertiesFrom()
    {
        PrimaryConstructorGenerator.AddPropertiesFrom(context, recordTypeDefinitionVariable, record, _recordSymbol);
        if (!_recordSymbol.IsValueType)
            return;
        
        foreach (var uniqueParameter in record.GetUniqueParameters(context))
        {
            RecordStructIsReadOnlyAttributeHandler(GetGetterMethodVar(_recordSymbol, uniqueParameter.Identifier.ValueText));
        }
    }

    private string AddEqualityOperator()
    {
        const string methodName = "op_Equality";
        
        context.WriteNewLine();
        context.WriteComment("operator ==");
        var equalsOperatorMethodVar = context.Naming.SyntheticVariable("equalityOperator", ElementKind.Method);
        var declaringTypeName = _recordSymbol.OriginalDefinition.ToDisplayString();
        var resolutionContext = _recordSymbol.NewTypeResolutionContextFor(ResolveTargetKind.Parameter);
        IReadOnlyList<ParameterSpec> parameters = [
            new("left", context.TypeResolver.Resolve(_recordSymbol, resolutionContext), RefKind.None, Constants.ParameterAttributes.None)  { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?" },
            new("right", context.TypeResolver.Resolve(_recordSymbol, resolutionContext), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?" }
        ];
        IList<string> typeParameters = [];
        var ilContext = context.ApiDriver.NewIlContext(context, methodName, equalsOperatorMethodVar);
        var equalsOperatorMethodExps = context.ApiDefinitionsFactory.Method(
                                                                        context, 
                                                                        new BodiedMemberDefinitionContext(methodName, equalsOperatorMethodVar, recordTypeDefinitionVariable, MemberOptions.Static, ilContext), 
                                                                        declaringTypeName, 
                                                                        Constants.Cecil.PublicOverrideOperatorAttributes, 
                                                                        parameters, 
                                                                        typeParameters, 
                                                                        BooleanReturnTypeResolver,
                                                                        out var equalityOperatorMethodReference);
        
        context.Generate(equalsOperatorMethodExps);
        
        InstructionRepresentation[] equalsBodyInstructions = _recordSymbol.IsValueType 
            ? [
                OpCodes.Ldarga_S.WithOperand(context.DefinitionVariables.GetVariable("left", VariableMemberKind.Parameter, methodName).VariableName.AsToken()),
                OpCodes.Ldarg_1,
                OpCodes.Call.WithOperand(_recordTypeEqualsOverloadMethodVar.AsToken()),
                OpCodes.Ret
            ] 
            : [
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Beq_S.WithBranchOperand("Equal"),
                OpCodes.Ldarg_0,
                OpCodes.Brfalse_S.WithBranchOperand("NotEqual"),
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Callvirt.WithOperand(_recordTypeEqualsOverloadMethodVar.AsToken()),
                OpCodes.Ret,
                OpCodes.Ldc_I4_0.WithInstructionMarker("NotEqual"),
                OpCodes.Ret,
                OpCodes.Ldc_I4_1.WithInstructionMarker("Equal"),
                OpCodes.Ret
            ];

        var bodyExps = context.ApiDefinitionsFactory.MethodBody(context, methodName, ilContext, [], equalsBodyInstructions);
        context.Generate(bodyExps);
        AddCompilerGeneratedAttributeTo(context, equalsOperatorMethodVar, VariableMemberKind.Method);
        
        if (!_recordSymbol.IsValueType)
            AddNullableContextAttributeTo(equalsOperatorMethodVar, NullableAwareness.Nullable);

        return equalityOperatorMethodReference.VariableName;
    }
    
    private void AddInequalityOperator(string equalityOperatorMethod)
    {
        var recordSymbol = _recordSymbol;
        const string methodName = "op_Inequality";
        
        context.WriteNewLine();
        context.WriteComment("operator !=");
        var inequalityOperatorMethodVar = context.Naming.SyntheticVariable($"inequalityOperator", ElementKind.Method);
        var declaringTypeName = recordSymbol.OriginalDefinition.ToDisplayString();
        var resolvedRecordType = context.TypeResolver.Resolve(recordSymbol, recordSymbol.NewTypeResolutionContextFor(ResolveTargetKind.Parameter));
        IReadOnlyList<ParameterSpec> parameters = 
        [
            new("left", resolvedRecordType, RefKind.None, Constants.ParameterAttributes.None)  { RegistrationTypeName = $"{_recordSymbol.OriginalDefinition.ToDisplayString()}?" },
            new("right", resolvedRecordType, RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{_recordSymbol.OriginalDefinition.ToDisplayString()}?" }
        ];

        var ilContext = context.ApiDriver.NewIlContext(context, methodName, inequalityOperatorMethodVar);
        var inequalityOperatorMethodExps = context.ApiDefinitionsFactory.Method(
                                                                                            context, 
                                                                                            new BodiedMemberDefinitionContext(methodName, inequalityOperatorMethodVar, recordTypeDefinitionVariable, MemberOptions.Static, ilContext), 
                                                                                            declaringTypeName, 
                                                                                            Constants.Cecil.PublicOverrideOperatorAttributes, 
                                                                                            parameters, 
                                                                                            [], 
                                                                                            BooleanReturnTypeResolver,
                                                                                            out _);
        
        context.Generate(inequalityOperatorMethodExps);

        var equalityMethodDefinitionVariable = equalityOperatorMethod;
        if (_recordSymbol is { IsGenericType: true })
        {
            var var = equalityMethodDefinitionVariable;
            equalityMethodDefinitionVariable = context.Naming.SyntheticVariable("equalsOperator", ElementKind.GenericInstance);
            context.Generate(
            [
                $"var {equalityMethodDefinitionVariable} = new MethodReference({var}.Name, {var}.ReturnType, {context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.TypeReference)}) {{ HasThis = {var}.HasThis, ExplicitThis = {var}.ExplicitThis, CallingConvention = {var}.CallingConvention, Parameters = {{ {var}.Parameters[0],  {var}.Parameters[1] }} }};",
            ]);
        }
        
        InstructionRepresentation[] inequalityBodyInstructions = 
        [
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Call.WithOperand(equalityMethodDefinitionVariable.AsToken()),
            OpCodes.Ldc_I4_0,
            OpCodes.Ceq,
            OpCodes.Ret
        ];

        context.Generate(context.ApiDefinitionsFactory.MethodBody(context, methodName, ilContext, [], inequalityBodyInstructions));
        
        AddCompilerGeneratedAttributeTo(context, inequalityOperatorMethodVar, VariableMemberKind.Method);
        if (!_recordSymbol.IsValueType)
            AddNullableContextAttributeTo(inequalityOperatorMethodVar, NullableAwareness.Nullable);
    }

    private void AddGetHashCodeMethod()
    {
        var recordSymbol = _recordSymbol;
        var getHashCodeMethodVar = context.Naming.SyntheticVariable("GetHashCode", ElementKind.Method);
        string declaringTypeName = _recordSymbol.OriginalDefinition.ToDisplayString();
        IReadOnlyList<ParameterSpec> parameters1 = [];
        IList<string> typeParameters = [];
        
        var ilContext = context.ApiDriver.NewIlContext(context, "GetHashCode", getHashCodeMethodVar);
        var getHashCodeMethodExps = context.ApiDefinitionsFactory.Method(
            context, 
            new BodiedMemberDefinitionContext("GetHashCode", getHashCodeMethodVar, recordTypeDefinitionVariable, MemberOptions.None, ilContext), 
            declaringTypeName, 
            Constants.Cecil.PublicOverrideMethodAttributes, 
            parameters1, 
            typeParameters, 
            Int32ReturnTypeResolver,
            out _);
        context.WriteNewLine();
        context.WriteComment(" GetHashCode()");
        context.Generate(getHashCodeMethodExps);

        const int HashCodeMultiplier = -1521134295;
        var getHashCodeMethodBodyExps = new List<InstructionRepresentation>();
        AddRecordClassHashCodeSpecificCode();

        var parameters = record.GetUniqueParameters(context);
        var lastParameter = parameters.Count > 0 ?  parameters[^1] : default;
        foreach(var parameter in parameters)
        {
            var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type!).Type.EnsureNotNull();
            var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, _recordSymbol.OriginalDefinition.ToDisplayString());
            var equalityComparerMembersForParamType = _equalityComparerMembersCache[parameterType.Name];

            var backingFieldRef = _recordSymbol is { IsGenericType: true } 
                ? $"new FieldReference({paramDefVar.VariableName}.Name, {paramDefVar.VariableName}.FieldType, {context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.TypeReference)})" 
                : paramDefVar.VariableName;

            getHashCodeMethodBodyExps.AddRange( 
            [
                OpCodes.Call.WithOperand(equalityComparerMembersForParamType.GetDefaultMethodVar.AsToken()), // EqualityComparer<{parameterType}}>.Default
                OpCodes.Ldarg_0, // Load this
                OpCodes.Ldfld.WithOperand(backingFieldRef.AsToken()), // load the backing field for the parameter
                OpCodes.Callvirt.WithOperand(equalityComparerMembersForParamType.GetHashCodeMethodVar.AsToken())
            ]);

            if (parameter != lastParameter)
            {
                getHashCodeMethodBodyExps.AddRange(
                [
                    OpCodes.Ldc_I4.WithOperand(HashCodeMultiplier),
                    OpCodes.Mul
                ]);
            }

            getHashCodeMethodBodyExps.Add(OpCodes.Add);
        }
        
        var bodyExps = context.ApiDefinitionsFactory.MethodBody(context, "GetHashCode", ilContext, [], [..getHashCodeMethodBodyExps, OpCodes.Ret]);
        context.Generate(bodyExps);
        context.WriteNewLine();
        
        AddCompilerGeneratedAttributeTo(context, getHashCodeMethodVar, VariableMemberKind.Method);
        AddIsReadOnlyAttributeTo(context, getHashCodeMethodVar);
        
        void AddRecordClassHashCodeSpecificCode()
        {
            if (_recordSymbol.IsValueType)
            {
                // Ensure that there's a 0 at the top of the stack to allow the caller to 
                // simply load another number and ADD them.
                getHashCodeMethodBodyExps.Add(OpCodes.Ldc_I4_0);
                return;
            }
            
            getHashCodeMethodBodyExps.Add(OpCodes.Ldc_I4.WithOperand(HashCodeMultiplier));
            if (HasBaseRecord(record))
            {
                // Initialize the hashcode with 'base.GetHashCode() * -1521134295'
                var baseGetHashCode = $$"""new MethodReference("GetHashCode", {{context.TypeResolver.Bcl.System.Int32}}, {{context.TypeResolver.Resolve(recordSymbol.BaseType, ResolveTargetKind.TypeReference)}}) { HasThis = true }""";
                getHashCodeMethodBodyExps.Add(OpCodes.Ldarg_0);
                getHashCodeMethodBodyExps.Add(OpCodes.Call.WithOperand(baseGetHashCode));
            }
            else
            {
                // Initialize the hashcode with 'EqualityComparer<Type>.Default.GetHashCode(EqualityContract) * -1521134295'
                var equalityComparerMembersForSystemType = _equalityComparerMembersCache[typeof(Type).Name];
                var getEqualityContractMethodVar = context.DefinitionVariables.GetVariable("get_EqualityContract", VariableMemberKind.Method, _recordSymbol.OriginalDefinition.ToDisplayString());
                getHashCodeMethodBodyExps.AddRange( 
                [
                    OpCodes.Call.WithOperand(equalityComparerMembersForSystemType.GetDefaultMethodVar.AsToken()),
                    OpCodes.Ldarg_0, // Load this
                    OpCodes.Call.WithOperand(MethodOnClosedGenericTypeForMethodVariable(getEqualityContractMethodVar.VariableName, recordTypeDefinitionVariable).AsToken()), // load EqualityContract
                    OpCodes.Callvirt.WithOperand(equalityComparerMembersForSystemType.GetHashCodeMethodVar.AsToken())
                ]);
            }
            getHashCodeMethodBodyExps.Add(OpCodes.Mul);
        }
    }

    private void AddToStringAndRelatedMethods()
    {
        var stringBuilderSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(StringBuilder).FullName!).EnsureNotNull();

        var stringBuilderAppendStringMethod = stringBuilderSymbol
                                                    .GetMembers("Append")
                                                    .OfType<IMethodSymbol>()
                                                    .SingleOrDefault(m => m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, context.RoslynTypeSystem.SystemString))
                                                    .MethodResolverExpression(context);
        
        AddPrintMembersMethod(stringBuilderAppendStringMethod, stringBuilderSymbol);
        AddToStringMethod();

        void AddToStringMethod()
        {
            const string ToStringName = "ToString";
            
            context.WriteNewLine();
            context.WriteComment($"{record.Identifier.ValueText}.{ToStringName}()");

            var toStringMethodVar = context.Naming.SyntheticVariable(ToStringName, ElementKind.Method);
            string declaringTypeName = _recordSymbol.OriginalDefinition.ToDisplayString();
            IReadOnlyList<ParameterSpec> parameters = [];
            IList<string> typeParameters = [];
            
            var ilContext = context.ApiDriver.NewIlContext(context, ToStringName, toStringMethodVar);
            var toStringDeclExps = context.ApiDefinitionsFactory.Method(
                                                                        context, 
                                                                        new BodiedMemberDefinitionContext(ToStringName, toStringMethodVar, recordTypeDefinitionVariable, MemberOptions.None, ilContext), 
                                                                        declaringTypeName, 
                                                                        Constants.Cecil.PublicOverrideMethodAttributes, 
                                                                        parameters, 
                                                                        typeParameters, 
                                                                        StringReturnTypeResolver,
                                                                        out var methodDefinitionVariable);
            
            context.Generate(toStringDeclExps);
            
            using var _ = context.DefinitionVariables.WithVariable(methodDefinitionVariable);
            var stringBuildDefaultCtor = stringBuilderSymbol.GetMembers(".ctor").OfType<IMethodSymbol>().Single(ctor => ctor.Parameters.Length == 0).MethodResolverExpression(context);

            InstructionRepresentation[] instructions = [
                OpCodes.Newobj.WithOperand(stringBuildDefaultCtor.AsToken()),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0,
                OpCodes.Ldstr.WithOperand($"\"{record.Identifier.ValueText}\""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod.AsToken()),
                OpCodes.Ldstr.WithOperand("\" { \""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod.AsToken()),
                OpCodes.Pop,
                OpCodes.Ldarg_0,
                OpCodes.Ldloc_0,
                (_recordSymbol.IsValueType ? OpCodes.Call : OpCodes.Callvirt).WithOperand(PrintMembersMethodToCall(stringBuilderSymbol).AsToken()), 
                OpCodes.Brfalse_S.WithBranchOperand("NoMemberToPrint"),
                OpCodes.Ldloc_0,
                OpCodes.Ldstr.WithOperand("\" \""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod.AsToken()),
                OpCodes.Pop,
                OpCodes.Ldloc_0.WithInstructionMarker("NoMemberToPrint"),
                OpCodes.Ldstr.WithOperand("\"}\""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod.AsToken()),
                
                OpCodes.Pop,
                OpCodes.Ldloc_0,
                
                OpCodes.Callvirt.WithOperand(stringBuilderSymbol.GetMembers("ToString").OfType<IMethodSymbol>().Single(m => m.Parameters.Length == 0).OverriddenMethod.MethodResolverExpression(context).AsToken()),
                OpCodes.Ret
            ];
            
            var toStringBodyExps = context.ApiDefinitionsFactory.MethodBody(
                                                                                context,
                                                                                ToStringName,
                                                                                ilContext, 
                                                                                [new("stringBuilder", context.TypeResolver.Resolve(stringBuilderSymbol, ResolveTargetKind.LocalVariable), toStringMethodVar)], 
                                                                                instructions);
            context.Generate(toStringBodyExps);
            AddCompilerGeneratedAttributeTo(context, toStringMethodVar, VariableMemberKind.Method);
            AddIsReadOnlyAttributeTo(context, toStringMethodVar);
        }
    }

    private string ClosedGenericMethodFor(string memberName, ResolvedType recordVar)
    {
        var methodVar = context.DefinitionVariables.GetVariable(memberName, VariableMemberKind.Method | VariableMemberKind.MethodReference, _recordSymbol.OriginalDefinition.ToDisplayString());
        return MethodOnClosedGenericTypeForMethodVariable(methodVar.VariableName, recordVar.Expression);
    }
    
    private string MethodOnClosedGenericTypeForMethodVariable(string methodVar, string recordVar, params ResolvedType[] parameterReferences)
    {
        if (_recordSymbol is { IsGenericType: true })
        {
            var parameters = parameterReferences.Length > 0 ? $$""", Parameters = { {{string.Join(',', parameterReferences.Select(p => $"new ParameterDefinition({p})"))}} }""" : "";
            return $"new MethodReference({methodVar}.Name, {methodVar}.ReturnType) {{ DeclaringType = {TypeOrClosedTypeFor(recordVar)}, HasThis = {methodVar}.HasThis, ExplicitThis = {methodVar}.ExplicitThis, CallingConvention = {methodVar}.CallingConvention{parameters} }}";
        }
            
        return methodVar;
    }
    
    private string TypeOrClosedTypeFor(string recordVar)
    {
        if (_recordSymbol is { IsGenericType: true } genericRecord)
        {
            var typeArguments = genericRecord.TypeParameters.Select(tp => context.TypeResolver.Resolve(tp, ResolveTargetKind.TypeReference)).ToArray();
            return context.TypeResolver.MakeGenericInstanceType(_recordSymbol.Name, new ResolvedType(recordVar), typeArguments, ResolveTargetKind.ReturnType).Expression;
        }
            
        return recordVar;
    }
    
    private static bool HasBaseRecord(IVisitorContext context, ITypeSymbol recordSymbol)
    {
        return !SymbolEqualityComparer.Default.Equals(recordSymbol.BaseType, context.RoslynTypeSystem.SystemObject) &&
               !SymbolEqualityComparer.Default.Equals(recordSymbol.BaseType, context.RoslynTypeSystem.SystemValueType);
    }

    private void AddIEquatableEquals()
    {
        context.WriteNewLine();
        context.WriteComment($"IEquatable<>.Equals({record.Identifier.ValueText} other)");
        var equalsVar = context.Naming.SyntheticVariable("Equals", ElementKind.Method);
        var declaringType = context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.TypeReference);

        string methodNameForParameterVariableRegistration = $"{_recordSymbol.OriginalDefinition.ToDisplayString()}.Equals";
        IReadOnlyList<ParameterSpec> parameters = [new("other", context.TypeResolver.Resolve(_recordSymbol, _recordSymbol.NewTypeResolutionContextFor(ResolveTargetKind.Parameter)), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?"} ];
        var ilContext = context.ApiDriver.NewIlContext(context, "Equals", equalsVar);
        var exps = context.ApiDefinitionsFactory.Method(
                                                context, 
                                                new BodiedMemberDefinitionContext("Equals", methodNameForParameterVariableRegistration, equalsVar, recordTypeDefinitionVariable, MemberOptions.None, ilContext), 
                                                _recordSymbol.OriginalDefinition.ToDisplayString(), 
                                                $"MethodAttributes.Public | MethodAttributes.HideBySig | {Constants.Cecil.InterfaceMethodDefinitionAttributes}", 
                                                parameters, 
                                                Array.Empty<string>(), 
                                                BooleanReturnTypeResolver,
                                                out var methodDefinitionVariable);

        context.Generate(exps);
        
        using (context.DefinitionVariables.WithVariable(methodDefinitionVariable))
        {
            var uniqueParameters = record.GetUniqueParameters(context);
            
            List<InstructionRepresentation> instructions = new();
            
            CheckForReferenceEqualityIfApplicable(instructions);

            // Compare each unique primary constructor parameter to compute equality.
            foreach (var parameter in uniqueParameters)
            {
                // Get the default comparer for the parameter type
                // IL_001a: call class [System.Collections]System.Collections.Generic.EqualityComparer`1<!0> class [System.Collections]System.Collections.Generic.EqualityComparer`1<int32>::get_Default()
                var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type!).Type.EnsureNotNull();
                instructions.Add(OpCodes.Call.WithOperand(_equalityComparerMembersCache[parameterType.Name].GetDefaultMethodVar.AsToken()));
                
                var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, _recordSymbol.OriginalDefinition.ToDisplayString());
                var backingFieldRef = _recordSymbol is { IsGenericType: true } 
                    ? $"new FieldReference({paramDefVar.VariableName}.Name, {paramDefVar.VariableName}.FieldType, {context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.TypeReference)})" 
                    : paramDefVar.VariableName;
                
                // load property backing field for 'this' 
                instructions.Add(OpCodes.Ldarg_0);
                instructions.Add(OpCodes.Ldfld.WithOperand(backingFieldRef.AsToken()));
                
                // load property backing field for 'other' 
                instructions.Add(OpCodes.Ldarg_1);
                instructions.Add(OpCodes.Ldfld.WithOperand(backingFieldRef.AsToken()));
                
                // compares both backing fields.
                instructions.Add(OpCodes.Callvirt.WithOperand(_equalityComparerMembersCache[parameterType.Name].EqualsMethodVar.AsToken()));
                instructions.Add(OpCodes.Brfalse.WithBranchOperand("NotEquals"));
            }
            
            instructions.AddRange(
            [
                OpCodes.Br_S.WithBranchOperand("ReferenceEquals"), // if the code reached this point all properties matched.
                OpCodes.Ldc_I4_0.WithInstructionMarker("NotEquals"),
                OpCodes.Ret,
                OpCodes.Ldc_I4_1.WithInstructionMarker("ReferenceEquals"),
                OpCodes.Ret
            ]);
            
            var equalsExps = context.ApiDefinitionsFactory.MethodBody(context, "Equals", ilContext, [], instructions.ToArray());
            context.Generate(equalsExps);
        }
        
        AddCompilerGeneratedAttributeTo(context, equalsVar, VariableMemberKind.Method);
        AddIsReadOnlyAttributeTo(context, equalsVar);

        if (_recordSymbol is { IsGenericType: true } genericRecord)
        {
            _recordTypeEqualsOverloadMethodVar = context.Naming.SyntheticVariable("Equals", ElementKind.GenericInstance);
            //TODO: Cecil specific
            context.Generate(
            [
                $$"""var {{_recordTypeEqualsOverloadMethodVar}} = new MethodReference({{equalsVar}}.Name, {{equalsVar}}.ReturnType, {{declaringType}}) { HasThis = {{equalsVar}}.HasThis, ExplicitThis = {{equalsVar}}.ExplicitThis, CallingConvention = {{equalsVar}}.CallingConvention };""",
                $$"""{{_recordTypeEqualsOverloadMethodVar}}.Parameters.Add({{equalsVar}}.Parameters[0]);"""
            ]);
        }
        else
        {
            _recordTypeEqualsOverloadMethodVar = methodDefinitionVariable.VariableName;
        }

        void CheckForReferenceEqualityIfApplicable(List<InstructionRepresentation> instructions)
        {
            if (_recordSymbol.IsValueType)
                return;
            
            instructions.AddRange(
            [
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Beq_S.WithBranchOperand("ReferenceEquals"),

                OpCodes.Ldarg_1,
                OpCodes.Brfalse_S.WithBranchOperand("NotEquals"),
            ]);

            var equalityContractGetter = MethodOnClosedGenericTypeForMethodVariable(_equalityContractGetMethodVar, recordTypeDefinitionVariable);
            instructions.AddRange(
            [
                OpCodes.Ldarg_0,
                OpCodes.Callvirt.WithOperand(equalityContractGetter.AsToken()),
                OpCodes.Ldarg_1,
                OpCodes.Callvirt.WithOperand(equalityContractGetter.AsToken()),
                OpCodes.Call.WithOperand(TypeEqualityOperator().AsToken()),
                OpCodes.Brfalse_S.WithBranchOperand("NotEquals")
            ]);
        }
    }

    private IDictionary<string, (string GetDefaultMethodVar, string EqualsMethodVar, string GetHashCodeMethodVar)> GenerateEqualityComparerMethods(IReadOnlyList<ITypeSymbol> targetTypes)
    {
        Dictionary<string, (string, string, string)> equalityComparerDataByType = new();
        foreach (var targetType in targetTypes)
        {
            var openEqualityComparerType = context.TypeResolver.Resolve(context.RoslynTypeSystem.ForType(typeof(EqualityComparer<>).FullName), ResolveTargetKind.TypeReference);
            if (equalityComparerDataByType.ContainsKey(targetType.Name))
                continue;
            
            var equalityComparerClosedType =  context.TypeResolver.MakeGenericInstanceType("EqualityComparer", openEqualityComparerType, [context.TypeResolver.Resolve(targetType, ResolveTargetKind.GenericTypeArgument)], ResolveTargetKind.TypeReference);
            var getDefaultMethodVar = EmitDefaultPropertyGetterMethod(targetType, equalityComparerClosedType);
            var equalsMethodVar = ResolveEqualsMethod(targetType, equalityComparerClosedType);
            var getHashCodeMethodVar = ResolveGetHashCodeMethod(targetType, equalityComparerClosedType);
            
            equalityComparerDataByType[targetType.Name] = (getDefaultMethodVar, equalsMethodVar, getHashCodeMethodVar);
        }

        return equalityComparerDataByType;

        string EmitDefaultPropertyGetterMethod(ITypeSymbol parameterType, ResolvedType equalityComparerOfParameterType)
        {
            var equalityComparerOfT = context.RoslynTypeSystem.SystemCollectionsGenericEqualityComparerOfT;
            var open = context.TypeResolver.Resolve(equalityComparerOfT, ResolveTargetKind.ReturnType);
            var resolvedOpenEqualityComparer = context.TypeResolver.MakeGenericInstanceType("xxxx", open, equalityComparerOfT, ResolveTargetKind.ReturnType);
            
            return context.MemberResolver.ResolveMethod(
                                            $"EqualityComparer<{parameterType.Name}>", 
                                            equalityComparerOfParameterType.Expression, 
                                            "get_Default", 
                                            resolvedOpenEqualityComparer, 
                                            [], 
                                            [], 
                                            MemberOptions.Static);
        }

        string ResolveEqualsMethod(ITypeSymbol parameterType, ResolvedType equalityComparerOfParameterType)
        {
            var resolvedTypeParameter = context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemCollectionsGenericEqualityComparerOfT.TypeParameters[0], ResolveTargetKind.Parameter);
            return context.MemberResolver.ResolveMethod(
                                    $"EqualityComparer<{parameterType.Name}>", 
                                    equalityComparerOfParameterType.Expression,
                                    "Equals",
                                    context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemBoolean, ResolveTargetKind.ReturnType), 
                                    [new ParameterSpec("x", resolvedTypeParameter, RefKind.None, Constants.ParameterAttributes.None), new ParameterSpec("y", resolvedTypeParameter, RefKind.None, Constants.ParameterAttributes.None)], 
                                    [], 
                                    MemberOptions.None);
        }
        
        string ResolveGetHashCodeMethod(ITypeSymbol parameterType, ResolvedType equalityComparerOfParameterType)
        {
            var resolvedTypeParameter = context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemCollectionsGenericEqualityComparerOfT.TypeParameters[0], ResolveTargetKind.Parameter);
            return context.MemberResolver.ResolveMethod(
                                                $"EqualityComparer<{parameterType.Name}>", 
                                                equalityComparerOfParameterType.Expression, 
                                                "GetHashCode", 
                                                context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemInt32, ResolveTargetKind.ReturnType), 
                                                [new ParameterSpec("obj", resolvedTypeParameter, RefKind.None, Constants.ParameterAttributes.None)], 
                                                [], 
                                                MemberOptions.None);
        }
    }

    private void AddEqualityContractPropertyIfNeeded()
    {
        if (record.IsKind(SyntaxKind.RecordStructDeclaration))
            return;

        const string propertyName = "EqualityContract";
        PropertyGenerator propertyGenerator = new(context);

        var equalityContractPropertyVar = context.Naming.SyntheticVariable(propertyName, ElementKind.Property);
        PropertyGenerationData propertyData = new(
            _recordSymbol.OriginalDefinition.ToDisplayString(),
            recordTypeDefinitionVariable,
            _recordSymbol,
            equalityContractPropertyVar,
            propertyName,
            new Dictionary<string, string> { ["get"] = "MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual" },
            false,
            resolveTargetKind => context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemType, resolveTargetKind),
            string.Empty, // used for registering the parameter for setters. In this case, there are none.
            Array.Empty<ParameterSpec>());

        _equalityContractGetMethodVar = context.Naming.SyntheticVariable("EqualityContract_get", ElementKind.Method);
        var ilContext = context.ApiDriver.NewIlContext(context, "EqualityContract_get", _equalityContractGetMethodVar);
        var definitionContext = new BodiedMemberDefinitionContext(propertyName, propertyData.Variable, recordTypeDefinitionVariable, MemberOptions.None, ilContext);
        var exps = context.ApiDefinitionsFactory.Property(context, definitionContext, record.Identifier.Text, [], propertyData.Type(ResolveTargetKind.Field));
        context.Generate(exps);

        using var _ = propertyGenerator.AddGetterMethodDeclaration(
                                                                in propertyData, 
                                                                _equalityContractGetMethodVar, 
                                                                false, // used for registering property parameters. In this case, there are none.
                                                                null,
                                                                ilContext);
        
        var getTypeFromHandleSymbol = (IMethodSymbol) context.RoslynTypeSystem.SystemType.GetMembers("GetTypeFromHandle").First();
        context.WriteNewLine();
        context.ApiDriver.WriteCilInstruction(context, ilContext, OpCodes.Ldtoken, recordTypeDefinitionVariable.AsToken());
        context.ApiDriver.WriteCilInstruction(context, ilContext, OpCodes.Call, getTypeFromHandleSymbol.MethodResolverExpression(context).AsToken());
        context.ApiDriver.WriteCilInstruction(context, ilContext, OpCodes.Ret);

        AddCompilerGeneratedAttributeTo(context, propertyData.Variable, VariableMemberKind.Method);
        AddIsReadOnlyAttributeTo(context, propertyData.Variable);
    }

    /// <summary>
    /// If <see cref="record"/> inherits from System.Object 2 Equals() overloads should be added:
    /// - Equals(object other) => Equals( (RecordType) other)
    /// - Equals(RecordType) is the same as IEquatable&lt;RecordType&gt;.Equals() so it is already implemented.
    /// 
    /// In cases where record inherits from another record a third Equals() overload is introduced:
    /// - Equals(BaseType other)  => Equals( (object) other)
    /// </summary>
    private void AddEqualsOverloads()
    {
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        const string methodName = "Equals";
        
        context.WriteNewLine();
        context.WriteComment("Equals(object)");
        var equalsObjectOverloadMethodVar = context.Naming.SyntheticVariable($"{methodName}ObjectOverload", ElementKind.Method);
        IReadOnlyList<ParameterSpec> parameters = [new("other", context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemObject, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None)];
        IList<string> typeParameters = [];
        var ilContext = context.ApiDriver.NewIlContext(context, methodName, equalsObjectOverloadMethodVar);
        var equalsObjectOverloadMethodExps = context.ApiDefinitionsFactory.Method(
                                                                        context, 
                                                                        new BodiedMemberDefinitionContext(methodName, equalsObjectOverloadMethodVar, recordTypeDefinitionVariable, MemberOptions.None, ilContext), 
                                                                        recordSymbol.Name, 
                                                                        Constants.Cecil.PublicOverrideMethodAttributes, 
                                                                        parameters, 
                                                                        typeParameters, 
                                                                        BooleanReturnTypeResolver,
                                                                        out _);
                                                                    
        context.Generate(equalsObjectOverloadMethodExps);

        var declaringTypeToken = context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.Instruction).AsToken();
        InstructionRepresentation[] equalsBodyInstructions =
            _recordSymbol.IsValueType 
                ? [
                    OpCodes.Ldarg_1,
                    OpCodes.Isinst.WithOperand(declaringTypeToken),
                    OpCodes.Brfalse_S.WithBranchOperand("NotEqual"),
                    OpCodes.Ldarg_0,
                    OpCodes.Ldarg_1,
                    OpCodes.Unbox_Any.WithOperand(declaringTypeToken),
                    OpCodes.Call.WithOperand(_recordTypeEqualsOverloadMethodVar.AsToken()),
                    OpCodes.Ret,
                    OpCodes.Ldc_I4_1.WithInstructionMarker("NotEqual"),
                    OpCodes.Ret
                  ]
                : [
                    OpCodes.Ldarg_0,
                    OpCodes.Ldarg_1,
                    OpCodes.Isinst.WithOperand(declaringTypeToken),
                    OpCodes.Callvirt.WithOperand(_recordTypeEqualsOverloadMethodVar.AsToken()),
                    OpCodes.Ret
                  ];

        var bodyExps = context.ApiDefinitionsFactory.MethodBody(context, methodName, ilContext, [], equalsBodyInstructions);
        context.Generate(bodyExps);
        AddCompilerGeneratedAttributeTo(context, equalsObjectOverloadMethodVar, VariableMemberKind.Method);
        AddIsReadOnlyAttributeTo(context, equalsObjectOverloadMethodVar);
        AddNullableContextAttributeTo(equalsObjectOverloadMethodVar, _recordSymbol.IsValueType ? NullableAwareness.NullableOblivious : NullableAwareness.Nullable);
        
        if (!HasBaseRecord(record))
            return;
        
        var equalsBaseOverloadMethodVar = context.Naming.SyntheticVariable($"{methodName}{recordSymbol.BaseType?.Name}Overload", ElementKind.Method);
        context.WriteNewLine();
        context.WriteComment($"Equals({recordSymbol.BaseType?.Name})");
        IReadOnlyList<ParameterSpec> baseEqualsParameters = [new("other", context.TypeResolver.Resolve(recordSymbol.BaseType, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{recordSymbol.BaseType?.Name}?" }];
        var equalsBaseOverloadIlContext = context.ApiDriver.NewIlContext(context, methodName, equalsBaseOverloadMethodVar);
        var equalsBaseOverloadMethodExps = context.ApiDefinitionsFactory.Method(
                                                                    context, 
                                                                    new BodiedMemberDefinitionContext(methodName, equalsBaseOverloadMethodVar, recordTypeDefinitionVariable, MemberOptions.None, equalsBaseOverloadIlContext), 
                                                                    recordSymbol.Name, 
                                                                    Constants.Cecil.PublicOverrideMethodAttributes, 
                                                                    baseEqualsParameters, 
                                                                    [], 
                                                                    BooleanReturnTypeResolver,
                                                                    out _);
        
        context.Generate(equalsBaseOverloadMethodExps);
        
        InstructionRepresentation[] equalsBodyInstructions2 = 
        [
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Callvirt.WithOperand(equalsObjectOverloadMethodVar.AsToken()),
            OpCodes.Ret
        ];

        context.Generate(context.ApiDefinitionsFactory.MethodBody(context, methodName, equalsBaseOverloadIlContext, [], equalsBodyInstructions2));
        AddCompilerGeneratedAttributeTo(context, equalsBaseOverloadMethodVar, VariableMemberKind.Method);
        AddIsReadOnlyAttributeTo(context, equalsBaseOverloadMethodVar);
        AddNullableContextAttributeTo(equalsBaseOverloadMethodVar, _recordSymbol.IsValueType ? NullableAwareness.NullableOblivious : NullableAwareness.Nullable);
    }

    private void AddDeconstructMethod()
    {
        if (record.ParameterList?.Parameters.Count is null or 0)
            return;
        
        var recordSymbol = _recordSymbol;
        const string methodName = "Deconstruct";

        Debug.Assert(record.ParameterList != null);
        var parameterSymbols = record.ParameterList.Parameters.Select(p => context.SemanticModel.GetDeclaredSymbol(p)).ToArray();
        var parameterTypeParamSpec = parameterSymbols.Select(parameter => new ParameterSymbolParameterSpec(parameter!, context, "") { RefKind = RefKind.Out} ).ToArray();
        
        context.WriteNewLine();
        context.WriteComment($"Deconstruct({string.Join(',', parameterSymbols.Select(parameterType => $"out {parameterType!.Type}"))})");
        var deconstructMethodVar = context.Naming.SyntheticVariable(methodName, ElementKind.Method);
        IList<string> typeParameters = [];
        var ilContext = context.ApiDriver.NewIlContext(context, methodName, deconstructMethodVar);
        var deconstructMethodVarExps = context.ApiDefinitionsFactory.Method(
                                                                context,
                                                                new BodiedMemberDefinitionContext(methodName, deconstructMethodVar, recordTypeDefinitionVariable, MemberOptions.None, ilContext), 
                                                                recordSymbol.OriginalDefinition.ToDisplayString(), 
                                                                Constants.Cecil.PublicInstanceMethod, 
                                                                parameterTypeParamSpec, 
                                                                typeParameters, 
                                                                VoidReturnTypeResolver,
                                                                out _);
        context.Generate(deconstructMethodVarExps);

        List<InstructionRepresentation> deconstructInstructions = new();
        int argIndex = 1;
        foreach (var p in parameterSymbols)
        {
            deconstructInstructions.Add(OpCodes.Ldarg.WithOperand(argIndex.ToString().AsToken()));
            deconstructInstructions.Add(OpCodes.Ldarg_0);
            deconstructInstructions.Add(OpCodes.Call.WithOperand(GetGetterMethodVar(recordSymbol, p.Name).AsToken()));
            
            var stindOpCode = p.Type.StindOpCodeFor();
            deconstructInstructions.Add(stindOpCode == OpCodes.Stobj ? stindOpCode.WithOperand(context.TypeResolver.Resolve(p.Type, ResolveTargetKind.Instruction).Expression.AsToken()) : stindOpCode);
            argIndex++;
        }
        deconstructInstructions.Add(OpCodes.Ret);

        context.Generate(context.ApiDefinitionsFactory.MethodBody(context, methodName, ilContext, [], deconstructInstructions.ToArray()));
        AddCompilerGeneratedAttributeTo(context, deconstructMethodVar, VariableMemberKind.Method);
        AddIsReadOnlyAttributeTo(context, deconstructMethodVar);
    }
    
    string GetGetterMethodVar(ITypeSymbol candidate, string propertyName)
    {
        var tbf = new MethodDefinitionVariable(candidate.OriginalDefinition.ToDisplayString(), $"get_{propertyName}", [], []) { Kind = VariableMemberKind.Method | VariableMemberKind.MethodReference };
        var getterMethodVar = context.DefinitionVariables.GetMethodVariable(tbf);
        if (getterMethodVar.IsValid)
        {
            if (candidate is INamedTypeSymbol { IsGenericType: true })
            {
                var var = getterMethodVar.VariableName;
                return $"new MethodReference({var}.Name, {var}.ReturnType, {context.TypeResolver.Resolve(candidate, ResolveTargetKind.TypeReference)}) {{ HasThis = {var}.HasThis, ExplicitThis = {var}.ExplicitThis, CallingConvention = {var}.CallingConvention }}";
            }
                
            return getterMethodVar.VariableName;
        }
            
        // getter is not defined in the declaring record; this means the primary constructor parameter
        // was passed to its base type ctor, lets validate that and retrieve the getter from the base.
        if (SymbolEqualityComparer.Default.Equals(_recordSymbol.BaseType, context.RoslynTypeSystem.SystemObject))
            throw new InvalidOperationException($"Variable for the getter method for {_recordSymbol.Name}.{propertyName} could not be found.");
            
        return GetGetterMethodVar(candidate.BaseType, propertyName);
    }
    
    private static bool HasBaseRecord(TypeDeclarationSyntax record)
    {
        if (record.BaseList?.Types.Count is 0 or null)
            return false;
        
        var baseRecordName = ((SimpleNameSyntax) record.BaseList!.Types.First().Type).Identifier;
        var found = record.Ancestors().OfType<CompilationUnitSyntax>().Single().DescendantNodes().SingleOrDefault(candidate => 
                                                                                            candidate is RecordDeclarationSyntax candidateBase 
                                                                                            && candidateBase.Identifier.ValueText == baseRecordName.ValueText
                                                                                            && candidateBase.IsKind(SyntaxKind.RecordDeclaration));

        return found != null;
    }

    private void AddCompilerGeneratedAttributeTo(IVisitorContext context, string memberVariable, VariableMemberKind memberKind)
    {
        var compilerGeneratedAttributeCtor = context.RoslynTypeSystem.SystemRuntimeCompilerServicesCompilerGeneratedAttribute.Ctor();
        var exps = context.ApiDefinitionsFactory.Attribute(context, compilerGeneratedAttributeCtor, "compilerGenerated", memberVariable, memberKind);
        context.WriteNewLine();
        context.Generate(exps);
    }
    
    private void AddIsReadOnlyAttributeTo(IVisitorContext context, string memberVariable) => _isReadOnlyAttributeHandler(context, _recordSymbol, memberVariable);

    private void InitializeIsReadOnlyAttributeHandler(ITypeSymbol recordSymbol)
    {
        _isReadOnlyAttributeHandler = recordSymbol.IsValueType ? (context1, recordSymbol1, memberVar) => RecordStructIsReadOnlyAttributeHandler(memberVar) : RecordClassIsReadOnlyAttributeHandler;
    }

    private static void RecordClassIsReadOnlyAttributeHandler(IVisitorContext context, ITypeSymbol recordSymbol, string memberVar) { }
    
    private void RecordStructIsReadOnlyAttributeHandler(string memberVar)
    {
        var isReadOnlyAttributeCtor = context.RoslynTypeSystem.IsReadOnlyAttribute.Ctor();
        var exps = context.ApiDefinitionsFactory.Attribute(context, isReadOnlyAttributeCtor, "isReadOnly", memberVar, VariableMemberKind.Method);
        context.WriteNewLine();
        context.Generate(exps);
    }
    
    private string TypeEqualityOperator()
    {
        var typeEqualityOperator = context.RoslynTypeSystem.SystemType.GetMembers("op_Equality").OfType<IMethodSymbol>().Single(Has2SystemTypeParameters).MethodResolverExpression(context);
        return typeEqualityOperator;
        
        bool Has2SystemTypeParameters(IMethodSymbol candidate) => 
            candidate.Parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, candidate.Parameters[1].Type);
    }
    
    private void AddNullableContextAttributeTo(string memberVar, NullableAwareness value)
    {
        var nullableContextAttributeCtor = context.RoslynTypeSystem.ForType<NullableContextAttribute>().Ctor(context.RoslynTypeSystem.SystemByte);
        var nullableContextAttrExps = context.ApiDefinitionsFactory.Attribute(context, nullableContextAttributeCtor,"nullableContext", memberVar, VariableMemberKind.Method, new CustomAttributeArgument { Value = (byte) value });
        context.Generate(nullableContextAttrExps);
    }
    
    private void InitializeEqualityComparerMemberCache()
    {
        var targetTypes = record.GetUniqueParameters(context)
            .Select(parameterType => context.SemanticModel.GetDeclaredSymbol(parameterType).EnsureNotNull<ISymbol, IParameterSymbol>().Type)
            .Append(context.RoslynTypeSystem.SystemType) // generates EqualityComparer member references for System.Type
            .ToArray();
        
        _equalityComparerMembersCache = GenerateEqualityComparerMethods(targetTypes);
    }
    
    private static ResolvedType BooleanReturnTypeResolver(IVisitorContext ctx) => ctx.TypeResolver.Resolve(ctx.RoslynTypeSystem.SystemBoolean, ResolveTargetKind.ReturnType);
    private static ResolvedType VoidReturnTypeResolver(IVisitorContext ctx) => ctx.TypeResolver.Resolve(ctx.RoslynTypeSystem.SystemVoid, ResolveTargetKind.ReturnType);
    private static ResolvedType Int32ReturnTypeResolver(IVisitorContext ctx) => ctx.TypeResolver.Resolve(ctx.RoslynTypeSystem.SystemInt32, ResolveTargetKind.ReturnType);
    private static ResolvedType StringReturnTypeResolver(IVisitorContext ctx) => ctx.TypeResolver.Resolve(ctx.RoslynTypeSystem.SystemString, ResolveTargetKind.ReturnType);
}
