using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.CodeGeneration.Extensions;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.CodeGeneration;

internal class RecordGenerator
{
    private readonly IVisitorContext context;
    private readonly string recordTypeDefinitionVariable;
    private readonly RecordDeclarationSyntax record;
    private string _equalityContractGetMethodVar;
    private string _recordTypeEqualsOverloadMethodVar;
    private IDictionary<string, (string GetDefaultMethodVar, string EqualsMethodVar, string GetHashCodeMethodVar)> _equalityComparerMembersCache;
    private ITypeSymbol _recordSymbol;
    private Action<IVisitorContext, ITypeSymbol, string> _isReadOnlyAttributeHandler;

    public RecordGenerator(IVisitorContext context, string recordTypeDefinitionVariable, RecordDeclarationSyntax record)
    {
        this.context = context;
        this.recordTypeDefinitionVariable = recordTypeDefinitionVariable;
        this.record = record;
    }

    public void AddNullabilityAttributesToTypeDefinition(string definitionVar)
    {
        var nullableAttributeCtor = context.RoslynTypeSystem.ForType<NullableAttribute>()
                                                        .GetMembers(".ctor")
                                                        .OfType<IMethodSymbol>()
                                                        .Single(ctor => ctor.Parameters.Length == 1 && ctor.Parameters[0].Type.SpecialType == SpecialType.System_Byte)
                                                        .MethodResolverExpression(context);

        var nullableAwareness = ((int) NullableAwareness.NullableOblivious).ToString();
        var nullableAttrExps = CecilDefinitionsFactory.Attribute("nullable", definitionVar, context, nullableAttributeCtor, [(context.TypeResolver.Bcl.System.Int32, nullableAwareness)]);
        context.WriteCecilExpressions(nullableAttrExps);

        AddNullableContextAttributeTo(definitionVar, NullableAwareness.NonNullable);
    }
    
    internal void AddSyntheticMembers()
    {
        _recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();

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
        AddEqualityOperator();
        AddInequalityOperator();
        AddDeconstructMethod();
    }

    private void AddCloneMethod()
    {
        if (_recordSymbol.IsValueType || record.ParameterList?.Parameters.Count == 0)
            return;
        
        const string CloneMethodName = "<Clone>$";
        context.WriteComment($"{_recordSymbol.Name} {CloneMethodName} method");

        var cloneMethodVar = context.Naming.SyntheticVariable("clone", ElementKind.Method);
        var cloneMethodExps = CecilDefinitionsFactory.Method(context, cloneMethodVar, CloneMethodName, Constants.Cecil.HideBySigNewSlotVirtual.AppendModifier("MethodAttributes.Public"), _recordSymbol, false, []);
        context.WriteCecilExpressions(
        [
            ..cloneMethodExps,
            $"{recordTypeDefinitionVariable}.Methods.Add({cloneMethodVar});"
        ]);

        var copyCtorVarToFind = _recordSymbol.GetMembers(".ctor").OfType<IMethodSymbol>().Single(c => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, c.ContainingType)).AsMethodDefinitionVariable();
        var copyCtorVar = context.DefinitionVariables.GetMethodVariable(copyCtorVarToFind);
        if (!copyCtorVar.IsValid)
        {
            throw new Exception($"Copy constructor definition variable for record {_recordSymbol.Name} could not be found.");
        }

        InstructionRepresentation[] instructions = 
        [
            OpCodes.Ldarg_0,
            OpCodes.Newobj.WithOperand(ClosedGenericMethodForMethodVariable(copyCtorVar.VariableName, recordTypeDefinitionVariable, context.TypeResolver.Resolve(_recordSymbol))),
            OpCodes.Ret
        ];
        
        context.WriteCecilExpressions(
            CecilDefinitionsFactory.MethodBody(context.Naming, "clone", cloneMethodVar, context.Naming.ILProcessor("clone"), [], instructions)
        );
        
        AddCompilerGeneratedAttributeTo(context, cloneMethodVar);
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
            var copyCtor = CecilDefinitionsFactory.Constructor(context, copyCtorVar, _recordSymbol.Name, false, Constants.Cecil.CtorAttributes.AppendModifier("MethodAttributes.Family | MethodAttributes.HideBySig"), [_recordSymbol.ToDisplayString()]);
            context.WriteCecilExpressions(
            [
                copyCtor,
                ..CecilDefinitionsFactory.Parameter("other", RefKind.None, false, copyCtorVar, context.Naming.Parameter("other"), context.TypeResolver.Resolve(_recordSymbol), Constants.ParameterAttributes.None, (null, false))
            ]);
        }

        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({copyCtorVar});");
        context.WriteNewLine();

        List<InstructionRepresentation> instructions = new();
        if (HasBaseRecord(context, _recordSymbol))
        {
            instructions.Add(OpCodes.Ldarg_0);
            instructions.Add(OpCodes.Ldarg_1);
            var tbf = _recordSymbol.BaseType!.GetMembers(".ctor").OfType<IMethodSymbol>().Single(c => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, c.ContainingType)).AsMethodDefinitionVariable();
            var baseCopyCtorVar = context.DefinitionVariables.GetMethodVariable(tbf);
            instructions.Add(OpCodes.Call.WithOperand(baseCopyCtorVar));
        }
        else
        {
            var baseCtor = context.RoslynTypeSystem.SystemObject.GetMembers(".ctor").OfType<IMethodSymbol>().Single(c => c.Parameters.Length == 0).MethodResolverExpression(context);
            instructions.Add(OpCodes.Ldarg_0);
            instructions.Add(OpCodes.Call.WithOperand(baseCtor));
        }
        
        foreach (var parameter in record.GetUniqueParameters(context))
        {
            var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, _recordSymbol.Name);
            var backingFieldRef = _recordSymbol is INamedTypeSymbol { IsGenericType: true } 
                    ? $"new FieldReference({paramDefVar.VariableName}.Name, {paramDefVar.VariableName}.FieldType, {context.TypeResolver.Resolve(_recordSymbol)})" 
                    : paramDefVar.VariableName;
                
            instructions.Add(OpCodes.Ldarg_0);
            
            // load property backing field for 'other' 
            instructions.Add(OpCodes.Ldarg_1);
            instructions.Add(OpCodes.Ldfld.WithOperand(backingFieldRef));
                
            // stores to property backing field on 'this' 
            instructions.Add(OpCodes.Stfld.WithOperand(backingFieldRef));
        }
        instructions.Add(OpCodes.Ret);
        
        context.WriteCecilExpressions(
            CecilDefinitionsFactory.MethodBody(context.Naming, Constants.Cecil.InstanceConstructorName, copyCtorVar, context.Naming.ILProcessor(Constants.Cecil.InstanceConstructorName), [], instructions.ToArray())
        );
        
        AddCompilerGeneratedAttributeTo(context, copyCtorVar);
    }

    private void AddPropertiesFrom()
    {
        PrimaryConstructorGenerator.AddPropertiesFrom(context, recordTypeDefinitionVariable, record);
        if (!_recordSymbol.IsValueType)
            return;
        
        foreach (var uniqueParameter in record.GetUniqueParameters(context))
        {
            RecordStructIsReadOnlyAttributeHandler(GetGetterMethodVar(context, _recordSymbol, uniqueParameter.Identifier.ValueText));
        }
    }

    private void AddEqualityOperator()
    {
        const string methodName = "op_Equality";
        
        context.WriteNewLine();
        context.WriteComment("operator ==");
        var equalsOperatorMethodVar = context.Naming.SyntheticVariable($"equalsOperator", ElementKind.Method);
        var equalsOperatorMethodExps = CecilDefinitionsFactory.Method(
            context,
            _recordSymbol.Name,
            equalsOperatorMethodVar,
            methodName,
            methodName,
            Constants.Cecil.PublicOverrideOperatorAttributes,
            [
                new ParameterSpec("left", context.TypeResolver.Resolve(_recordSymbol), RefKind.None, Constants.ParameterAttributes.None)  { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?" },
                new ParameterSpec("right", context.TypeResolver.Resolve(_recordSymbol), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?" }
            ],
            [],
            ctx => ctx.TypeResolver.Bcl.System.Boolean,
            out _);
        
        context.WriteCecilExpressions(equalsOperatorMethodExps);
        
        InstructionRepresentation[] equalsBodyInstructions = _recordSymbol.IsValueType 
            ? [
                OpCodes.Ldarga_S.WithOperand(context.DefinitionVariables.GetVariable("left", VariableMemberKind.Parameter, methodName)),
                OpCodes.Ldarg_1,
                OpCodes.Call.WithOperand(_recordTypeEqualsOverloadMethodVar),
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
                OpCodes.Callvirt.WithOperand(_recordTypeEqualsOverloadMethodVar),
                OpCodes.Ret,
                OpCodes.Ldc_I4_0.WithInstructionMarker("NotEqual"),
                OpCodes.Ret,
                OpCodes.Ldc_I4_1.WithInstructionMarker("Equal"),
                OpCodes.Ret
            ];
        
        var bodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, methodName, equalsOperatorMethodVar, [], equalsBodyInstructions);
        context.WriteCecilExpressions(bodyExps);
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({equalsOperatorMethodVar});");
        AddCompilerGeneratedAttributeTo(context, equalsOperatorMethodVar);
        
        if (!_recordSymbol.IsValueType)
            AddNullableContextAttributeTo(equalsOperatorMethodVar, NullableAwareness.Nullable);
    }
    
    private void AddInequalityOperator()
    {
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        const string methodName = "op_Inequality";
        
        context.WriteNewLine();
        context.WriteComment("operator !=");
        var inequalityOperatorMethodVar = context.Naming.SyntheticVariable($"inequalityOperator", ElementKind.Method);
        var inequalityOperatorMethodExps = CecilDefinitionsFactory.Method(
            context,
            recordSymbol.Name,
            inequalityOperatorMethodVar,
            methodName,
            methodName,
            Constants.Cecil.PublicOverrideOperatorAttributes,
            [
                new ParameterSpec("left", context.TypeResolver.Resolve(recordSymbol), RefKind.None, Constants.ParameterAttributes.None)  { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?" },
                new ParameterSpec("right", context.TypeResolver.Resolve(recordSymbol), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?" }
            ],
            [],
            ctx => ctx.TypeResolver.Bcl.System.Boolean,
            out _);
        
        context.WriteCecilExpressions(inequalityOperatorMethodExps);

        var equalityMethodDefinitionVariable = context.DefinitionVariables.GetMethodVariable(new MethodDefinitionVariable(recordSymbol.Name, "op_Equality", [$"{_recordSymbol.ToDisplayString()}?", $"{_recordSymbol.ToDisplayString()}?"], 0)).VariableName;
        if (_recordSymbol is INamedTypeSymbol { IsGenericType: true })
        {
            var var = equalityMethodDefinitionVariable;
            equalityMethodDefinitionVariable = context.Naming.SyntheticVariable("equalsOperator", ElementKind.GenericInstance);
            context.WriteCecilExpressions(
            [
                $"var {equalityMethodDefinitionVariable} = new MethodReference({var}.Name, {var}.ReturnType, {context.TypeResolver.Resolve(_recordSymbol)}) {{ HasThis = {var}.HasThis, ExplicitThis = {var}.ExplicitThis, CallingConvention = {var}.CallingConvention, Parameters = {{ {var}.Parameters[0],  {var}.Parameters[1] }} }};",
            ]);
        }
        
        InstructionRepresentation[] inequalityBodyInstructions = 
        [
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Call.WithOperand(equalityMethodDefinitionVariable),
            OpCodes.Ldc_I4_0,
            OpCodes.Ceq,
            OpCodes.Ret
        ];
        
        var bodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, methodName, inequalityOperatorMethodVar, [], inequalityBodyInstructions);
        context.WriteCecilExpressions(bodyExps);
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({inequalityOperatorMethodVar});");
        AddCompilerGeneratedAttributeTo(context, inequalityOperatorMethodVar);
        if (!_recordSymbol.IsValueType)
            AddNullableContextAttributeTo(inequalityOperatorMethodVar, NullableAwareness.Nullable);
    }

    private void AddGetHashCodeMethod()
    {
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        
        var getHashCodeMethodVar = context.Naming.SyntheticVariable("GetHashCode", ElementKind.Method);
        var getHashCodeMethodExps = CecilDefinitionsFactory.Method(
                                                                            context,
                                                                            record.Identifier.ValueText, 
                                                                            getHashCodeMethodVar,
                                                                            "GetHashCode",
                                                                            "GetHashCode",
                                                                            Constants.Cecil.PublicOverrideMethodAttributes,
                                                                            [],
                                                                            [],
                                                                            ctx => ctx.TypeResolver.Bcl.System.Int32,
                                                                            out var methodDefinitionVariable);
        context.WriteNewLine();
        context.WriteComment(" GetHashCode()");
        context.WriteCecilExpressions(getHashCodeMethodExps);

        const string HashCodeMultiplier = "-1521134295";
        var getHashCodeMethodBodyExps = new List<InstructionRepresentation>();
        AddRecordClassHashCodeSpecificCode();

        var parameters = record.GetUniqueParameters(context);
        var lastParameter = parameters.Count > 0 ?  parameters[^1] : default;
        foreach(var parameter in parameters)
        {
            var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type!).Type.EnsureNotNull();
            var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, record.Identifier.ValueText());
            var equalityComparerMembersForParamType = _equalityComparerMembersCache[parameterType.Name];

            var backingFieldRef = _recordSymbol is INamedTypeSymbol { IsGenericType: true } 
                ? $"new FieldReference({paramDefVar.VariableName}.Name, {paramDefVar.VariableName}.FieldType, {context.TypeResolver.Resolve(_recordSymbol)})" 
                : paramDefVar.VariableName;

            getHashCodeMethodBodyExps.AddRange( 
            [
                OpCodes.Call.WithOperand(equalityComparerMembersForParamType.GetDefaultMethodVar), // EqualityComparer<{parameterType}}>.Default
                OpCodes.Ldarg_0, // Load this
                OpCodes.Ldfld.WithOperand(backingFieldRef), // load the backing field for the parameter
                OpCodes.Callvirt.WithOperand(equalityComparerMembersForParamType.GetHashCodeMethodVar)
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
        
        var ilVar = context.Naming.ILProcessor("GetHashCode");
        var bodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, "GetHashCode", getHashCodeMethodVar, ilVar, [], [..getHashCodeMethodBodyExps, OpCodes.Ret]);
        context.WriteCecilExpressions(bodyExps);
        context.WriteNewLine();
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({getHashCodeMethodVar});");
        context.WriteNewLine();
        
        AddCompilerGeneratedAttributeTo(context, getHashCodeMethodVar);
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
                var baseGetHashCode = $$"""new MethodReference("GetHashCode", {{context.TypeResolver.Bcl.System.Int32}}, {{context.TypeResolver.Resolve(recordSymbol.BaseType)}}) { HasThis = true }""";
                getHashCodeMethodBodyExps.Add(OpCodes.Ldarg_0);
                getHashCodeMethodBodyExps.Add(OpCodes.Call.WithOperand(baseGetHashCode));
            }
            else
            {
                // Initialize the hashcode with 'EqualityComparer<Type>.Default.GetHashCode(EqualityContract) * -1521134295'
                var equalityComparerMembersForSystemType = _equalityComparerMembersCache[typeof(Type).Name];
                var getEqualityContractMethodVar = context.DefinitionVariables.GetVariable("get_EqualityContract", VariableMemberKind.Method, record.Identifier.ValueText());
                getHashCodeMethodBodyExps.AddRange( 
                [
                    OpCodes.Call.WithOperand(equalityComparerMembersForSystemType.GetDefaultMethodVar),
                    OpCodes.Ldarg_0, // Load this
                    OpCodes.Call.WithOperand(ClosedGenericMethodForMethodVariable(getEqualityContractMethodVar.VariableName, recordTypeDefinitionVariable)), // load EqualityContract
                    OpCodes.Callvirt.WithOperand(equalityComparerMembersForSystemType.GetHashCodeMethodVar)
                ]);
            }
            getHashCodeMethodBodyExps.Add(OpCodes.Mul);
        }
    }

    private void AddToStringAndRelatedMethods()
    {
        const string PrintMembersMethodName = "PrintMembers";
        var stringBuilderSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(StringBuilder).FullName!).EnsureNotNull();

        var stringBuilderAppendStringMethod = stringBuilderSymbol
            .GetMembers("Append")
            .OfType<IMethodSymbol>()
            .SingleOrDefault(m => m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, context.RoslynTypeSystem.SystemString))
            .MethodResolverExpression(context);
        
        var printMembersVar = context.Naming.SyntheticVariable(PrintMembersMethodName, ElementKind.Method);

        AddPrintMembersMethod();
        AddToStringMethod();

        void AddPrintMembersMethod()
        {
            context.WriteNewLine();
            context.WriteComment($"{record.Identifier.ValueText}.{PrintMembersMethodName}()");

            var builderParameter = new ParameterSpec("builder", context.TypeResolver.Resolve(typeof(StringBuilder).FullName), RefKind.None, Constants.ParameterAttributes.None);
            var printMembersDeclExps = CecilDefinitionsFactory.Method(
                                                        context, 
                                                        record.Identifier.ValueText, 
                                                        printMembersVar, 
                                                        PrintMembersMethodName, 
                                                        PrintMembersMethodName,
                                                        $"MethodAttributes.Family | { (HasBaseRecord(record) ? Constants.Cecil.HideBySigVirtual : Constants.Cecil.HideBySigNewSlotVirtual) }",
                                                        [builderParameter],
                                                        [],
                                                        ctx => context.TypeResolver.Bcl.System.Boolean,
                                                        out var methodDefinitionVariable);

            using var _ = context.DefinitionVariables.WithVariable(methodDefinitionVariable);
            context.WriteCecilExpressions([
                ..printMembersDeclExps,
                $"{recordTypeDefinitionVariable}.Methods.Add({printMembersVar});"
            ]);

            List<InstructionRepresentation> bodyInstructions = HasBaseRecord(context, _recordSymbol)
                ? 
                [
                    OpCodes.Ldarg_0,
                    OpCodes.Ldarg_1,
                    OpCodes.Call.WithOperand(PrintMembersMethodToCall()),
                    OpCodes.Brfalse_S.WithBranchOperand("DoNotAppendComma"),
                    OpCodes.Ldarg_1,
                    OpCodes.Ldstr.WithOperand("\", \""),
                    OpCodes.Callvirt.WithOperand(stringBuilderAppendStringMethod),
                    OpCodes.Pop,
                    OpCodes.Nop.WithInstructionMarker("DoNotAppendComma")
                ]
                : [];

            var separator = string.Empty;
            foreach (var property in _recordSymbol.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility == Accessibility.Public))
            {
                var stringBuilderAppendMethod = StringBuilderAppendMethodFor(context, property.Type, stringBuilderSymbol);
                if (stringBuilderAppendMethod == null)
                {
                    context.WriteComment($"Property '{property.Name}' of type {property.Type.Name} not supported in PrintMembers()/ToString() ");
                    context.WriteComment("Only primitives, string and object are supported. The implementation of PrintMembers()/ToString() is definitely incomplete.");
                    continue;
                }
                
                bodyInstructions.AddRange(
                [
                    OpCodes.Ldarg_1,
                    OpCodes.Ldstr.WithOperand($"\"{separator}{property.Name} = \""),
                    OpCodes.Callvirt.WithOperand(stringBuilderAppendStringMethod),
                    OpCodes.Pop,
                    OpCodes.Ldarg_1,
                    OpCodes.Ldarg_0,
                    OpCodes.Call.WithOperand(ClosedGenericMethodFor($"get_{property.Name}", recordTypeDefinitionVariable)),
                    OpCodes.Box.WithOperand(context.TypeResolver.Resolve(property.Type)).IgnoreIf(property.Type.TypeKind != TypeKind.TypeParameter),
                    OpCodes.Callvirt.WithOperand(stringBuilderAppendMethod.MethodResolverExpression(context)),
                    OpCodes.Pop
                ]);
                separator = ", ";
            }

            // TODO: Print public fields
            // foreach (var field in recordSymbol.GetMembers().OfType<IFieldSymbol>())
            // {
            //     Console.WriteLine(field);
            // }
            
            var printMemberBodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, PrintMembersMethodName, printMembersVar, [], 
            [
                ..bodyInstructions,
                OpCodes.Ldc_I4_1,
                OpCodes.Ret
            ]);
            context.WriteCecilExpressions(printMemberBodyExps);
            AddCompilerGeneratedAttributeTo(context, printMembersVar);
            AddIsReadOnlyAttributeTo(context, printMembersVar);
        }

        void AddToStringMethod()
        {
            const string ToStringName = "ToString";
            
            context.WriteNewLine();
            context.WriteComment($"{record.Identifier.ValueText}.{ToStringName}()");

            var toStringMethodVar = context.Naming.SyntheticVariable(ToStringName, ElementKind.Method);
            var toStringDeclExps = CecilDefinitionsFactory.Method(
                context,
                record.Identifier.ValueText,
                toStringMethodVar,
                ToStringName,
                ToStringName,
                Constants.Cecil.PublicOverrideMethodAttributes,
                [],
                [],
                ctx => context.TypeResolver.Bcl.System.String,
                out var methodDefinitionVariable);
            
            context.WriteCecilExpressions([
                ..toStringDeclExps,
                $"{recordTypeDefinitionVariable}.Methods.Add({toStringMethodVar});"
            ]);
            
            using var _ = context.DefinitionVariables.WithVariable(methodDefinitionVariable);
            var stringBuildDefaultCtor = stringBuilderSymbol.GetMembers(".ctor").OfType<IMethodSymbol>().Single(ctor => ctor.Parameters.Length == 0).MethodResolverExpression(context);
            
            var toStringBodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, ToStringName, toStringMethodVar, [context.TypeResolver.Resolve(stringBuilderSymbol)],
            [
                OpCodes.Newobj.WithOperand(stringBuildDefaultCtor),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0,
                OpCodes.Ldstr.WithOperand($"\"{record.Identifier.ValueText}\""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod),
                OpCodes.Ldstr.WithOperand("\" { \""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod),
                OpCodes.Pop,
                OpCodes.Ldarg_0,
                OpCodes.Ldloc_0,
                (_recordSymbol.IsValueType ? OpCodes.Call : OpCodes.Callvirt).WithOperand(PrintMembersMethodToCall()), 
                OpCodes.Brfalse_S.WithBranchOperand("NoMemberToPrint"),
                OpCodes.Ldloc_0,
                OpCodes.Ldstr.WithOperand("\" \""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod),
                OpCodes.Pop,
                OpCodes.Ldloc_0.WithInstructionMarker("NoMemberToPrint"),
                OpCodes.Ldstr.WithOperand("\"}\""),
                OpCodes.Call.WithOperand(stringBuilderAppendStringMethod),
                
                OpCodes.Pop,
                OpCodes.Ldloc_0,
                
                OpCodes.Callvirt.WithOperand(stringBuilderSymbol.GetMembers("ToString").OfType<IMethodSymbol>().Single(m => m.Parameters.Length == 0).MethodResolverExpression(context)),
                OpCodes.Ret
            ]);
            context.WriteCecilExpressions(toStringBodyExps);
            AddCompilerGeneratedAttributeTo(context, toStringMethodVar);
            AddIsReadOnlyAttributeTo(context, toStringMethodVar);
        }
        
        // Returns a `MethodReference` for the PrintMembers() method to be invoked
        // If the record inherits from another record returns the base `PrintMembers()` method
        string PrintMembersMethodToCall()
        {
            if (_recordSymbol is INamedTypeSymbol { IsGenericType: true } genericRecord)
                return $$"""new MethodReference("PrintMembers", {{context.TypeResolver.Bcl.System.Boolean}}, {{context.TypeResolver.Resolve(_recordSymbol)}}) { HasThis = true, Parameters = { new ParameterDefinition({{context.TypeResolver.Resolve(stringBuilderSymbol)}}) } }""";
            
            return HasBaseRecord(context, _recordSymbol) 
                ? $$"""new MethodReference("PrintMembers", {{context.TypeResolver.Bcl.System.Boolean}}, {{context.TypeResolver.Resolve(_recordSymbol.BaseType)}}) { HasThis = true, Parameters = { new ParameterDefinition({{ context.TypeResolver.Resolve(stringBuilderSymbol) }}) } }"""
                : printMembersVar;
        }

        static IMethodSymbol StringBuilderAppendMethodFor(IVisitorContext context, ITypeSymbol type, ITypeSymbol stringBuilderSymbol)
        {
            var stringBuilderAppendMethod = AppendMethodFor(stringBuilderSymbol, type);
            if (stringBuilderAppendMethod != null)
                return stringBuilderAppendMethod;

            // if type is a generic type parameter or a class use the `object` overload instead.
            return type.TypeKind == TypeKind.TypeParameter || !type.IsValueType 
                ? AppendMethodFor(stringBuilderSymbol, context.RoslynTypeSystem.SystemObject)  
                :null;
            
            static IMethodSymbol AppendMethodFor(ITypeSymbol typeSymbol, ITypeSymbol type)
            {
                var stringBuilderAppendMethod = typeSymbol
                    .GetMembers("Append")
                    .OfType<IMethodSymbol>()
                    .SingleOrDefault(m => m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type));
                return stringBuilderAppendMethod;
            }
        }
    }

    private string ClosedGenericMethodFor(string memberName, string recordVar)
    {
        var methodVar = context.DefinitionVariables.GetVariable(memberName, VariableMemberKind.Method, _recordSymbol.Name);
        return ClosedGenericMethodForMethodVariable(methodVar.VariableName, recordVar);
    }
    
    private string ClosedGenericMethodForMethodVariable(string methodVar, string recordVar, params string[] parameterReferences)
    {
        if (_recordSymbol is INamedTypeSymbol { IsGenericType: true } genericRecord)
        {
            var parameters = parameterReferences.Length > 0 ? $$""", Parameters = { {{string.Join(',', parameterReferences.Select(p => $"new ParameterDefinition({p})"))}} }""" : "";
            var typeArguments = string.Join(',', genericRecord.TypeParameters.Select(tp => context.TypeResolver.Resolve(tp)));
            return $"new MethodReference({methodVar}.Name, {methodVar}.ReturnType) {{ DeclaringType = {recordVar}.MakeGenericInstanceType([{typeArguments}]), HasThis = {methodVar}.HasThis, ExplicitThis = {methodVar}.ExplicitThis, CallingConvention = {methodVar}.CallingConvention{parameters} }}";
        }
            
        return methodVar;
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
        var declaringType = context.TypeResolver.Resolve(_recordSymbol);
        
        var exps = CecilDefinitionsFactory.Method(
                                    context,
                                    record.Identifier.ValueText(),
                                    equalsVar,
                                    $"{record.Identifier.ValueText()}.Equals", "Equals",
                                    $"MethodAttributes.Public | MethodAttributes.HideBySig | {Constants.Cecil.InterfaceMethodDefinitionAttributes}",
                                    [new ParameterSpec("other", declaringType, RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{_recordSymbol.ToDisplayString()}?"} ],
                                    Array.Empty<string>(),
                                    ctx => ctx.TypeResolver.Bcl.System.Boolean, out var methodDefinitionVariable);

        context.WriteCecilExpressions([..exps, $"{recordTypeDefinitionVariable}.Methods.Add({equalsVar});"]);
        
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
                instructions.Add(OpCodes.Call.WithOperand(_equalityComparerMembersCache[parameterType.Name].GetDefaultMethodVar));
                
                var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, _recordSymbol.Name);
                var backingFieldRef = _recordSymbol is INamedTypeSymbol { IsGenericType: true } 
                    ? $"new FieldReference({paramDefVar.VariableName}.Name, {paramDefVar.VariableName}.FieldType, {context.TypeResolver.Resolve(_recordSymbol)})" 
                    : paramDefVar.VariableName;
                
                // load property backing field for 'this' 
                instructions.Add(OpCodes.Ldarg_0);
                instructions.Add(OpCodes.Ldfld.WithOperand(backingFieldRef));
                
                // load property backing field for 'other' 
                instructions.Add(OpCodes.Ldarg_1);
                instructions.Add(OpCodes.Ldfld.WithOperand(backingFieldRef));
                
                // compares both backing fields.
                instructions.Add(OpCodes.Callvirt.WithOperand(_equalityComparerMembersCache[parameterType.Name].EqualsMethodVar));
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
            
            var ilVar = context.Naming.ILProcessor("Equals");
            var equalsExps = CecilDefinitionsFactory.MethodBody(context.Naming, "Equals",equalsVar, ilVar, [], instructions.ToArray());
            context.WriteCecilExpressions(equalsExps);
        }
        
        AddCompilerGeneratedAttributeTo(context, equalsVar);
        AddIsReadOnlyAttributeTo(context, equalsVar);

        if (_recordSymbol is INamedTypeSymbol { IsGenericType: true } genericRecord)
        {
            _recordTypeEqualsOverloadMethodVar = context.Naming.SyntheticVariable("Equals", ElementKind.GenericInstance);
            context.WriteCecilExpressions(
            [
                $$"""var {{_recordTypeEqualsOverloadMethodVar}} = new MethodReference({{equalsVar}}.Name, {{equalsVar}}.ReturnType, {{declaringType}}) { HasThis = {{equalsVar}}.HasThis, ExplicitThis = {{equalsVar}}.ExplicitThis, CallingConvention = {{equalsVar}}.CallingConvention };""",
                $$"""{{_recordTypeEqualsOverloadMethodVar}}.Parameters.Add({{equalsVar}}.Parameters[0]);"""
            ]);
        }
        else
        {
            _recordTypeEqualsOverloadMethodVar = equalsVar;
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

            var equalityContractGetter = ClosedGenericMethodForMethodVariable(_equalityContractGetMethodVar, recordTypeDefinitionVariable);
            instructions.AddRange(
            [
                OpCodes.Ldarg_0,
                OpCodes.Callvirt.WithOperand(equalityContractGetter),
                OpCodes.Ldarg_1,
                OpCodes.Callvirt.WithOperand(equalityContractGetter),
                OpCodes.Call.WithOperand(TypeEqualityOperator()),
                OpCodes.Brfalse_S.WithBranchOperand("NotEquals")
            ]);
        }
    }

    private IDictionary<string, (string GetDefaultMethodVar, string EqualsMethodVar, string GetHashCodeMethodVar)> GenerateEqualityComparerMethods(IReadOnlyList<ITypeSymbol> targetTypes)
    {
        Dictionary<string, (string, string, string)> equalityComparerDataByType = new();
        
        foreach (var targetType in targetTypes)
        {
            var openEqualityComparerType = context.TypeResolver.Resolve(context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(EqualityComparer<>).FullName!));
            if (equalityComparerDataByType.ContainsKey(targetType.Name))
                continue;
            
            var equalityComparerOfParameterType = openEqualityComparerType.MakeGenericInstanceType(context.TypeResolver.Resolve(targetType));
            var openGetDefaultMethodVar = context.Naming.SyntheticVariable("openget_Default", ElementKind.LocalVariable);

            var getDefaultMethodVar = EmitDefaultPropertyGetterMethod(targetType, openGetDefaultMethodVar, equalityComparerOfParameterType);
            var equalsMethodVar = EmitEqualsMethod(equalityComparerOfParameterType);
            var getHashCodeMethodVar = EmitGetHashCodeMethod(equalityComparerOfParameterType);
            
            equalityComparerDataByType[targetType.Name] = (getDefaultMethodVar, equalsMethodVar, getHashCodeMethodVar);
        }

        return equalityComparerDataByType;

        string EmitDefaultPropertyGetterMethod(ITypeSymbol parameterType, string openGetDefaultMethodVar, string equalityComparerOfParameterType)
        {
            var getDefaultMethodVar = context.Naming.SyntheticVariable($"get_Default_{parameterType.Name}", ElementKind.MemberReference);
            string[] defaultPropertyGetterExps =
            [
                $$"""var {{openGetDefaultMethodVar}} = assembly.MainModule.ImportReference(typeof(System.Collections.Generic.EqualityComparer<>)).Resolve().Methods.First(m => m.Name == "get_Default");""",
                $$"""var {{getDefaultMethodVar}} = new MethodReference("get_Default", assembly.MainModule.ImportReference({{openGetDefaultMethodVar}}).ReturnType)""",
                "{",
                $"\tDeclaringType = {equalityComparerOfParameterType},",
                $"\tHasThis = {openGetDefaultMethodVar}.HasThis,",
                $"\tExplicitThis = {openGetDefaultMethodVar}.ExplicitThis,",
                $"\tCallingConvention = {openGetDefaultMethodVar}.CallingConvention,",
                "};"
            ];
            context.WriteCecilExpressions(defaultPropertyGetterExps);
            return getDefaultMethodVar;
        }

        string EmitEqualsMethod(string equalityComparerOfParameterType)
        {
            var equalsMethodVar = context.Naming.SyntheticVariable("Equals", ElementKind.MemberReference);
            var equalityComparerOpenEqualsMethodVar = context.Naming.SyntheticVariable("Equals", ElementKind.LocalVariable);
            string[] equalityComparerEqualsMethodExps = [
                $$"""var {{equalityComparerOpenEqualsMethodVar}} = assembly.MainModule.ImportReference(typeof(System.Collections.Generic.EqualityComparer<>)).Resolve().Methods.First(m => m.Name == "Equals");""",
                $"""var {equalsMethodVar} = new MethodReference("Equals", assembly.MainModule.ImportReference({equalityComparerOpenEqualsMethodVar}).ReturnType)""",
                "{",
                $"\tDeclaringType = {equalityComparerOfParameterType},",
                $"\tHasThis = {equalityComparerOpenEqualsMethodVar}.HasThis,",
                $"\tExplicitThis = {equalityComparerOpenEqualsMethodVar}.ExplicitThis,",
                $"\tCallingConvention = {equalityComparerOpenEqualsMethodVar}.CallingConvention",
                "};",
                $"{equalsMethodVar}.Parameters.Add({equalityComparerOpenEqualsMethodVar}.Parameters[0]);",
                $"{equalsMethodVar}.Parameters.Add({equalityComparerOpenEqualsMethodVar}.Parameters[1]);"
            ];
            context.WriteCecilExpressions(equalityComparerEqualsMethodExps);
            return equalsMethodVar;
        }
        
        string EmitGetHashCodeMethod(string equalityComparerOfParameterType)
        {
            const string methodName = "GetHashCode";
            var getHashCodeMethodVar = context.Naming.SyntheticVariable(methodName, ElementKind.MemberReference);
            var equalityComparerOpenGetHashCodeMethodVar = context.Naming.SyntheticVariable(methodName, ElementKind.LocalVariable);
            string[] equalityComparerGetHashCodeMethodExps = 
            [
                $$"""var {{equalityComparerOpenGetHashCodeMethodVar}} = assembly.MainModule.ImportReference(typeof(System.Collections.Generic.EqualityComparer<>)).Resolve().Methods.First(m => m.Name == "GetHashCode");""",
                $"""var {getHashCodeMethodVar} = new MethodReference("GetHashCode", assembly.MainModule.ImportReference({equalityComparerOpenGetHashCodeMethodVar}).ReturnType)""",
                "{",
                $"\tDeclaringType = {equalityComparerOfParameterType},",
                $"\tHasThis = {equalityComparerOpenGetHashCodeMethodVar}.HasThis,",
                $"\tExplicitThis = {equalityComparerOpenGetHashCodeMethodVar}.ExplicitThis,",
                $"\tCallingConvention = {equalityComparerOpenGetHashCodeMethodVar}.CallingConvention",
                "};",
                $"{getHashCodeMethodVar}.Parameters.Add({equalityComparerOpenGetHashCodeMethodVar}.Parameters[0]);"
            ];
            context.WriteCecilExpressions(equalityComparerGetHashCodeMethodExps);
            return getHashCodeMethodVar;
        }
    }

    private void AddEqualityContractPropertyIfNeeded()
    {
        if (record.IsKind(SyntaxKind.RecordStructDeclaration))
            return;

        //var hasBaseRecord = HasBaseRecord(record);

        const string propertyName = "EqualityContract";
        PropertyGenerator propertyGenerator = new(context);

        var equalityContractPropertyVar = context.Naming.SyntheticVariable(propertyName, ElementKind.Property);
        PropertyGenerationData propertyData = new(
            record.Identifier.ValueText,
            recordTypeDefinitionVariable,
            record.TypeParameterList?.Parameters.Count > 0,
            equalityContractPropertyVar,
            propertyName,
            new Dictionary<string, string> { ["get"] = "MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual" },
            false,
            context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemType),
            string.Empty, // used for registering the parameter for setters. In this case, there are none.
            Array.Empty<ParameterSpec>());
        
        var exps = CecilDefinitionsFactory.PropertyDefinition(propertyData.Variable, propertyName, propertyData.ResolvedType);
        context.WriteCecilExpressions(
        [
            ..exps,
            $"{propertyData.DeclaringTypeVariable}.Properties.Add({propertyData.Variable});"
        ]);

        _equalityContractGetMethodVar = context.Naming.SyntheticVariable("EqualityContract_get", ElementKind.Method);
        using var _ = propertyGenerator.AddGetterMethodDeclaration(
                                                                in propertyData, 
                                                                _equalityContractGetMethodVar, 
                                                                false, 
                                                                string.Empty, // used for registering property parameters. In this case, there are none.
                                                                null);
        
        var getterIlVar = context.Naming.ILProcessor("EqualityContract_get");
        
        var getTypeFromHandleSymbol = (IMethodSymbol) context.RoslynTypeSystem.SystemType.GetMembers("GetTypeFromHandle").First();
        context.WriteCecilExpression($"var {getterIlVar} = {_equalityContractGetMethodVar}.Body.GetILProcessor();");
        context.WriteNewLine();
        context.EmitCilInstruction(getterIlVar, OpCodes.Ldtoken, recordTypeDefinitionVariable);
        context.EmitCilInstruction(getterIlVar, OpCodes.Call, getTypeFromHandleSymbol.MethodResolverExpression(context));
        context.EmitCilInstruction(getterIlVar, OpCodes.Ret);

        AddCompilerGeneratedAttributeTo(context, propertyData.Variable);
        AddIsReadOnlyAttributeTo(context, propertyData.Variable);
    }

    /// <summary>
    /// If <paramref name="record"/> inherits from System.Object 2 Equals() overloads should be added:
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
        var equalsObjectOverloadMethodExps = CecilDefinitionsFactory.Method(
            context,
            recordSymbol.Name,
            equalsObjectOverloadMethodVar,
            methodName,
            methodName,
            Constants.Cecil.PublicOverrideMethodAttributes,
            [new ParameterSpec("other", context.TypeResolver.Bcl.System.Object, RefKind.None, Constants.ParameterAttributes.None)],
            [],
            ctx => ctx.TypeResolver.Bcl.System.Boolean,
            out _);
        
        context.WriteCecilExpressions(equalsObjectOverloadMethodExps);
        
        InstructionRepresentation[] equalsBodyInstructions =
            _recordSymbol.IsValueType 
                ? [
                    OpCodes.Ldarg_1,
                    OpCodes.Isinst.WithOperand($"{_recordTypeEqualsOverloadMethodVar}.DeclaringType"),
                    OpCodes.Brfalse_S.WithBranchOperand("NotEqual"),
                    OpCodes.Ldarg_0,
                    OpCodes.Ldarg_1,
                    OpCodes.Unbox_Any.WithOperand($"{_recordTypeEqualsOverloadMethodVar}.DeclaringType"),
                    OpCodes.Call.WithOperand(_recordTypeEqualsOverloadMethodVar),
                    OpCodes.Ret,
                    OpCodes.Ldc_I4_1.WithInstructionMarker("NotEqual"),
                    OpCodes.Ret
                  ]
                : [
                    OpCodes.Ldarg_0,
                    OpCodes.Ldarg_1,
                    OpCodes.Isinst.WithOperand($"{_recordTypeEqualsOverloadMethodVar}.DeclaringType"),
                    OpCodes.Callvirt.WithOperand(_recordTypeEqualsOverloadMethodVar),
                    OpCodes.Ret
                  ];
        
        var bodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, methodName, equalsObjectOverloadMethodVar, [], equalsBodyInstructions);
        context.WriteCecilExpressions(bodyExps);
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({equalsObjectOverloadMethodVar});");
        AddCompilerGeneratedAttributeTo(context, equalsObjectOverloadMethodVar);
        AddIsReadOnlyAttributeTo(context, equalsObjectOverloadMethodVar);
        AddNullableContextAttributeTo(equalsObjectOverloadMethodVar, _recordSymbol.IsValueType ? NullableAwareness.NullableOblivious : NullableAwareness.Nullable);
        
        if (!HasBaseRecord(record))
            return;
        
        context.WriteNewLine();
        context.WriteComment($"Equals({recordSymbol.BaseType?.Name})");
        var equalsBaseOverloadMethodVar = context.Naming.SyntheticVariable($"{methodName}{recordSymbol.BaseType?.Name}Overload", ElementKind.Method);
        var equalsBaseOverloadMethodExps = CecilDefinitionsFactory.Method(
            context,
            recordSymbol.Name,
            equalsBaseOverloadMethodVar,
            methodName,
            methodName,
            Constants.Cecil.PublicOverrideMethodAttributes,
            [new ParameterSpec("other", context.TypeResolver.Resolve(recordSymbol.BaseType), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{recordSymbol.BaseType?.Name}?" }],
            [],
            ctx => ctx.TypeResolver.Bcl.System.Boolean,
            out _);
        
        context.WriteCecilExpressions(equalsBaseOverloadMethodExps);
        
        InstructionRepresentation[] equalsBodyInstructions2 = 
        [
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Callvirt.WithOperand(equalsObjectOverloadMethodVar),
            OpCodes.Ret
        ];
        
        var bodyExps2 = CecilDefinitionsFactory.MethodBody(context.Naming, methodName, equalsBaseOverloadMethodVar, [], equalsBodyInstructions2);
        context.WriteCecilExpressions(bodyExps2);
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({equalsBaseOverloadMethodVar});");
        AddCompilerGeneratedAttributeTo(context, equalsBaseOverloadMethodVar);
        AddIsReadOnlyAttributeTo(context, equalsBaseOverloadMethodVar);
        AddNullableContextAttributeTo(equalsBaseOverloadMethodVar, _recordSymbol.IsValueType ? NullableAwareness.NullableOblivious : NullableAwareness.Nullable);
    }

    private void AddDeconstructMethod()
    {
        if (record.ParameterList?.Parameters.Count is null or 0)
            return;
        
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        const string methodName = "Deconstruct";

        var parametersInfo = record.ParameterList!.Parameters.Select(p => (p.Identifier.ValueText, context.SemanticModel.GetTypeInfo(p.Type!).Type));
        var parameterTypeParamSpec = parametersInfo.Select(parameterInfo => 
            new ParameterSpec(parameterInfo.ValueText, context.TypeResolver.Resolve(parameterInfo.Type), RefKind.Out, Constants.ParameterAttributes.Out) { RegistrationTypeName = parameterInfo.Type.ToDisplayString()})
            .ToArray();
        
        context.WriteNewLine();
        context.WriteComment($"Deconstruct({string.Join(',', parametersInfo.Select(parameterType => $"out {parameterType!.Type}"))})");
        var deconstructMethodVar = context.Naming.SyntheticVariable(methodName, ElementKind.Method);
        var deconstructMethodVarExps = CecilDefinitionsFactory.Method(
            context,
            recordSymbol.Name,
            deconstructMethodVar,
            methodName,
            methodName,
            Constants.Cecil.PublicInstanceMethod,
            parameterTypeParamSpec,
            [],
            ctx => ctx.TypeResolver.Bcl.System.Void,
            out _);
        
        context.WriteCecilExpressions(deconstructMethodVarExps);

        List<InstructionRepresentation> deconstructInstructions = new();
        int argIndex = 1;
        foreach (var p in parametersInfo)
        {
            deconstructInstructions.Add(OpCodes.Ldarg.WithOperand(argIndex.ToString()));
            deconstructInstructions.Add(OpCodes.Ldarg_0);
            deconstructInstructions.Add(OpCodes.Call.WithOperand(GetGetterMethodVar(context, recordSymbol, p.ValueText)));
            
            var stindOpCode = p.Type.StindOpCodeFor();
            deconstructInstructions.Add(stindOpCode == OpCodes.Stobj ? stindOpCode.WithOperand(context.TypeResolver.Resolve(p.Type)) : stindOpCode);
            argIndex++;
        }
        deconstructInstructions.Add(OpCodes.Ret);
        
        var bodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, methodName, deconstructMethodVar, [], deconstructInstructions.ToArray());
        context.WriteCecilExpressions(bodyExps);
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({deconstructMethodVar});");
        AddCompilerGeneratedAttributeTo(context, deconstructMethodVar);
        AddIsReadOnlyAttributeTo(context, deconstructMethodVar);
    }
    
    string GetGetterMethodVar(IVisitorContext context, ITypeSymbol candidate, string propertyName)
    {
        var getterMethodVar = context.DefinitionVariables.GetMethodVariable(new MethodDefinitionVariable(candidate.Name, $"get_{propertyName}", [], 0));
        if (getterMethodVar.IsValid)
        {
            if (candidate is INamedTypeSymbol { IsGenericType: true })
            {
                var var = getterMethodVar.VariableName;
                return $"new MethodReference({var}.Name, {var}.ReturnType, {context.TypeResolver.Resolve(candidate)}) {{ HasThis = {var}.HasThis, ExplicitThis = {var}.ExplicitThis, CallingConvention = {var}.CallingConvention }}";
            }
                
            return getterMethodVar.VariableName;
        }
            
        // getter is not defined in the declaring record; this means the primary constructor parameter
        // was passed to its base type ctor, lets validate that and retrieve the getter from the base.
        if (SymbolEqualityComparer.Default.Equals(_recordSymbol.BaseType, context.RoslynTypeSystem.SystemObject))
            throw new InvalidOperationException($"Variable for the getter method for {_recordSymbol.Name}.{propertyName} could not be found.");
            
        return GetGetterMethodVar(context, candidate.BaseType, propertyName);
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

    private void AddCompilerGeneratedAttributeTo(IVisitorContext context, string memberVariable)
    {
        var compilerGeneratedAttributeCtor = context.RoslynTypeSystem.SystemRuntimeCompilerServicesCompilerGeneratedAttribute.Ctor();
        var exps = CecilDefinitionsFactory.Attribute("compilerGenerated", memberVariable, context, compilerGeneratedAttributeCtor.MethodResolverExpression(context));
        context.WriteNewLine();
        context.WriteCecilExpressions(exps);
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
        var exps = CecilDefinitionsFactory.Attribute("isReadOnly", memberVar, context, isReadOnlyAttributeCtor.MethodResolverExpression(context));
        context.WriteNewLine();
        context.WriteCecilExpressions(exps);
    }
    
    private string TypeEqualityOperator()
    {
        var typeEqualityOperator = context.RoslynTypeSystem.SystemType.GetMembers("op_Equality")
            .OfType<IMethodSymbol>()
            .Single(Has2SystemTypeParameters).MethodResolverExpression(context);
        
        return typeEqualityOperator;
        
        bool Has2SystemTypeParameters(IMethodSymbol candidate) => 
            candidate.Parameters.Length == 2
            && SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, candidate.Parameters[1].Type);
    }
    
    private void AddNullableContextAttributeTo(string memberVar, NullableAwareness value)
    {
        var nullableContextAttributeCtor = context.RoslynTypeSystem.ForType<NullableContextAttribute>()
            .GetMembers(".ctor")
            .OfType<IMethodSymbol>()
            .Single(ctor => ctor.Parameters.Length == 1)
            .MethodResolverExpression(context);

        var nullableContextValue = ((int)value).ToString();
        var nullableContextAttrExps = CecilDefinitionsFactory.Attribute("nullableContext", memberVar, context, nullableContextAttributeCtor, [(context.TypeResolver.Bcl.System.Int32, nullableContextValue)]);
        context.WriteCecilExpressions(nullableContextAttrExps);
    }
    
    private void InitializeEqualityComparerMemberCache()
    {
        var targetTypes = record.GetUniqueParameters(context)
            .Select(parameterType => context.SemanticModel.GetDeclaredSymbol(parameterType).EnsureNotNull<ISymbol, IParameterSymbol>().Type)
            .Append(context.RoslynTypeSystem.SystemType) // generates EqualityComparer member references for System.Type
            .ToArray();
        
        _equalityComparerMembersCache = GenerateEqualityComparerMethods(targetTypes);
    }
}
