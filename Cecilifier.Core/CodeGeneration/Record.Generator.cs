using System;
using System.Collections.Generic;
using System.Linq;
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

public class RecordGenerator
{
    private string _equalityContractGetMethodVar;
    private IDictionary<string, (string GetDefaultMethodVar, string EqualsMethodVar, string GetHashCodeMethodVar)> _equalityComparerMembersCache;
    private string _recordTypeEqualsOverloadMethodVar;

    internal void AddSyntheticMembers(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        InitializeEqualityComparerMemberCache(context, record);
        
        AddEqualityContractPropertyIfNeeded(context, recordTypeDefinitionVariable, record);
        PrimaryConstructorGenerator.AddPropertiesFrom(context, recordTypeDefinitionVariable, record);
        PrimaryConstructorGenerator.AddPrimaryConstructor(context, recordTypeDefinitionVariable, record);
        AddIEquatableEquals(context, recordTypeDefinitionVariable, record);
        AddToStringAndRelatedMethods(context, recordTypeDefinitionVariable, record);
        AddGetHashCodeMethod(context, recordTypeDefinitionVariable, record);
        AddEqualsOverloads(context, recordTypeDefinitionVariable, record);
        AddEqualityOperator(context, recordTypeDefinitionVariable, record);
        AddInequalityOperator(context, recordTypeDefinitionVariable, record);
    }

    private void AddEqualityOperator(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        const string methodName = "op_Equality";
        
        context.WriteNewLine();
        context.WriteComment("operator ==");
        var equalsOperatorMethodVar = context.Naming.SyntheticVariable($"equalsOperator", ElementKind.Method);
        var equalsOperatorMethodExps = CecilDefinitionsFactory.Method(
            context,
            recordSymbol.Name,
            equalsOperatorMethodVar,
            methodName,
            methodName,
            Constants.Cecil.PublicOverrideOperatorAttributes,
            [
                new ParameterSpec("left", context.TypeResolver.Resolve(recordSymbol), RefKind.None, Constants.ParameterAttributes.None)  { RegistrationTypeName = $"{record.Identifier.ValueText}?" },
                new ParameterSpec("right", context.TypeResolver.Resolve(recordSymbol), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{record.Identifier.ValueText}?" }
            ],
            [],
            ctx => ctx.TypeResolver.Bcl.System.Boolean,
            out _);
        
        context.WriteCecilExpressions(equalsOperatorMethodExps);
        
        InstructionRepresentation[] equalsBodyInstructions = 
        [
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
    }
    
    private void AddInequalityOperator(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        const string methodName = "op_Inequality";
        
        context.WriteNewLine();
        context.WriteComment("operator ==");
        var inequalityOperatorMethodVar = context.Naming.SyntheticVariable($"inequalityOperator", ElementKind.Method);
        var inequalityOperatorMethodExps = CecilDefinitionsFactory.Method(
            context,
            recordSymbol.Name,
            inequalityOperatorMethodVar,
            methodName,
            methodName,
            Constants.Cecil.PublicOverrideOperatorAttributes,
            [
                new ParameterSpec("left", context.TypeResolver.Resolve(recordSymbol), RefKind.None, Constants.ParameterAttributes.None)  { RegistrationTypeName = $"{record.Identifier.ValueText}?" },
                new ParameterSpec("right", context.TypeResolver.Resolve(recordSymbol), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{record.Identifier.ValueText}?" }
            ],
            [],
            ctx => ctx.TypeResolver.Bcl.System.Boolean,
            out _);
        
        context.WriteCecilExpressions(inequalityOperatorMethodExps);

        var equalityMethodDefinitionVariable = context.DefinitionVariables.GetMethodVariable(new MethodDefinitionVariable(recordSymbol.Name, "op_Equality", [$"{record.Identifier.ValueText}?", $"{record.Identifier.ValueText}?"], 0));
        InstructionRepresentation[] inequalityBodyInstructions = 
        [
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Call.WithOperand(equalityMethodDefinitionVariable.VariableName),
            OpCodes.Ldc_I4_0,
            OpCodes.Ceq,
            OpCodes.Ret
        ];
        
        var bodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, methodName, inequalityOperatorMethodVar, [], inequalityBodyInstructions);
        context.WriteCecilExpressions(bodyExps);
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({inequalityOperatorMethodVar});");
    }

    private void InitializeEqualityComparerMemberCache(IVisitorContext context, TypeDeclarationSyntax record)
    {
        var targetTypes = record.GetUniqueParameters(context)
                                                            .Select(parameterType => context.SemanticModel.GetDeclaredSymbol(parameterType).EnsureNotNull<ISymbol, IParameterSymbol>().Type)
                                                            .Append(context.RoslynTypeSystem.SystemType) // generates EqualityComparer member references for System.Type
                                                            .ToArray();
        
        _equalityComparerMembersCache = GenerateEqualityComparerMethods(context, targetTypes);
    }

    private void AddGetHashCodeMethod(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
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
                OpCodes.Call.WithOperand(getEqualityContractMethodVar.VariableName), // load EqualityContract
                OpCodes.Callvirt.WithOperand(equalityComparerMembersForSystemType.GetHashCodeMethodVar)
            ]);
        }
        getHashCodeMethodBodyExps.Add(OpCodes.Mul);
        
        var parameters = record.GetUniqueParameters(context);
        var lastParameter = parameters.Count > 0 ?  parameters[^1] : default;
        foreach(var parameter in parameters)
        {
            var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type!).Type.EnsureNotNull();
            var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, record.Identifier.ValueText());
            var equalityComparerMembersForParamType = _equalityComparerMembersCache[parameterType.Name];

            getHashCodeMethodBodyExps.AddRange( 
            [
                OpCodes.Call.WithOperand(equalityComparerMembersForParamType.GetDefaultMethodVar), // EqualityComparer<{parameterType}}>.Default
                OpCodes.Ldarg_0, // Load this
                OpCodes.Ldfld.WithOperand(paramDefVar.VariableName), // load the backing field for the parameter
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
    }

    private void AddToStringAndRelatedMethods(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        const string PrintMembersMethodName = "PrintMembers";
        var stringBuilderSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(StringBuilder).FullName!).EnsureNotNull();

        var stringBuilderAppendStringMethod = stringBuilderSymbol
            .GetMembers("Append")
            .OfType<IMethodSymbol>()
            .SingleOrDefault(m => m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, context.RoslynTypeSystem.SystemString))
            .MethodResolverExpression(context);
        
        var printMembersVar = context.Naming.SyntheticVariable(PrintMembersMethodName, ElementKind.Method);
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        
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

            List<InstructionRepresentation> bodyInstructions = HasBaseRecord(context, recordSymbol)
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
            foreach (var property in recordSymbol.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility == Accessibility.Public))
            {
                var stringBuilderAppendMethod = stringBuilderSymbol
                                                    .GetMembers("Append")
                                                    .OfType<IMethodSymbol>()
                                                    .SingleOrDefault(m => m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, property.Type));

                if (stringBuilderAppendMethod == null)
                {
                    //TODO: Handle property type != string. Specifically value types and type parameters. The code need to call `ToString()`
                    //      Most likely, for non-value type/type parameters we can simply call ToString() on the instance.
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
                    OpCodes.Call.WithOperand(context.DefinitionVariables.GetVariable($"get_{property.Name}", VariableMemberKind.Method, record.Identifier.ValueText)),
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
                OpCodes.Callvirt.WithOperand(PrintMembersMethodToCall()), 
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
        }
        
        // Returns a `MethodReference` for the PrintMembers() method to be invoked
        // If the record inherits from another record returns the base `PrintMembers()` method
        string PrintMembersMethodToCall()
        {
            return HasBaseRecord(context, recordSymbol) 
                ? $$"""new MethodReference("PrintMembers", {{context.TypeResolver.Bcl.System.Boolean}}, {{context.TypeResolver.Resolve(recordSymbol.BaseType)}}) { HasThis = true, Parameters = { new ParameterDefinition({{ context.TypeResolver.Resolve(stringBuilderSymbol) }}) } }"""
                : printMembersVar;
        }
    }

    private static bool HasBaseRecord(IVisitorContext context, ITypeSymbol recordSymbol)
    {
        return !SymbolEqualityComparer.Default.Equals(recordSymbol.BaseType, context.RoslynTypeSystem.SystemObject) &&
               !SymbolEqualityComparer.Default.Equals(recordSymbol.BaseType, context.RoslynTypeSystem.SystemValueType);
    }

    //TODO: Record struct (no need to check for null, no need to check EqualityContract, etc)
    private void AddIEquatableEquals(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        context.WriteNewLine();
        context.WriteComment($"IEquatable<>.Equals({record.Identifier.ValueText} other)");
        _recordTypeEqualsOverloadMethodVar = context.Naming.SyntheticVariable("Equals", ElementKind.Method);
        var exps = CecilDefinitionsFactory.Method(
                                    context,
                                    record.Identifier.ValueText(),
                                    _recordTypeEqualsOverloadMethodVar,
                                    $"{record.Identifier.ValueText()}.Equals", "Equals",
                                    $"MethodAttributes.Public | MethodAttributes.HideBySig | {Constants.Cecil.InterfaceMethodDefinitionAttributes}", //TODO: No NEWSLOT if in derived record
                                    [new ParameterSpec("other", recordTypeDefinitionVariable, RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = $"{record.Identifier.ValueText}?"} ],
                                    Array.Empty<string>(),
                                    ctx => ctx.TypeResolver.Bcl.System.Boolean, out var methodDefinitionVariable);

        context.WriteCecilExpressions([..exps, $"{recordTypeDefinitionVariable}.Methods.Add({_recordTypeEqualsOverloadMethodVar});"]);
        
        // Compare each unique primary constructor parameter to compute equality.
        using (context.DefinitionVariables.WithVariable(methodDefinitionVariable))
        {
            var uniqueParameters = record.GetUniqueParameters(context);

            List<InstructionRepresentation> instructions = new();
            instructions.AddRange(
            [
                // reference records only 
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Beq_S.WithBranchOperand("ReferenceEquals"),
                
                OpCodes.Ldarg_1,
                OpCodes.Brfalse_S.WithBranchOperand("NotEquals"),
            ]);
            
            // TODO: reference records only
            instructions.AddRange(
            [
                OpCodes.Ldarg_0,
                OpCodes.Callvirt.WithOperand(_equalityContractGetMethodVar),
                OpCodes.Ldarg_1,
                OpCodes.Callvirt.WithOperand(_equalityContractGetMethodVar),
                OpCodes.Call.WithOperand(TypeEqualityOperator(context)),
                OpCodes.Brfalse_S.WithBranchOperand("NotEquals")
            ]);
            
            foreach (var parameter in uniqueParameters)
            {
                // Get the default comparer for the parameter type
                // IL_001a: call class [System.Collections]System.Collections.Generic.EqualityComparer`1<!0> class [System.Collections]System.Collections.Generic.EqualityComparer`1<int32>::get_Default()
                var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type!).Type.EnsureNotNull();
                instructions.Add(OpCodes.Call.WithOperand(_equalityComparerMembersCache[parameterType.Name].GetDefaultMethodVar));
                
                // load property backing field for 'this' 
                var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, record.Identifier.ValueText());
                instructions.Add(OpCodes.Ldarg_0);
                instructions.Add(OpCodes.Ldfld.WithOperand(paramDefVar.VariableName));
                
                // load property backing field for 'other' 
                instructions.Add(OpCodes.Ldarg_1);
                instructions.Add(OpCodes.Ldfld.WithOperand(paramDefVar.VariableName));
                
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
            var equalsExps = CecilDefinitionsFactory.MethodBody(context.Naming, "Equals",_recordTypeEqualsOverloadMethodVar, ilVar, [], instructions.ToArray());
            context.WriteCecilExpressions(equalsExps);
        }
    }

    private IDictionary<string, (string GetDefaultMethodVar, string EqualsMethodVar, string GetHashCodeMethodVar)> GenerateEqualityComparerMethods(IVisitorContext context, IReadOnlyList<ITypeSymbol> targetTypes)
    {
        Dictionary<string, (string, string, string)> equalityComparerDataByType = new();
        
        foreach (var targetType in targetTypes)
        {
            var openEqualityComparerType = context.TypeResolver.Resolve(context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(EqualityComparer<>).FullName!));
            //var parameterType = context.SemanticModel.GetTypeInfo(targetType.Type!).Type.EnsureNotNull();
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

    private void AddEqualityContractPropertyIfNeeded(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
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
    }
    
    /// <summary>
    /// If <paramref name="record"/> inherits from System.Object 2 Equals() overloads should be added:
    /// - Equals(object other) => Equals( (RecordType) other)
    /// - Equals(RecordType) is the same as IEquatable&lt;RecordType&gt;.Equals() so it is already implemented.
    /// 
    /// In cases where <paramref name="record"/> inherits from another record a third Equals() overload is introduced:
    /// - Equals(BaseType other)  => Equals( (object) other)
    /// </summary>
    /// <param name="context"></param>
    /// <param name="recordTypeDefinitionVariable"></param>
    /// <param name="record"></param>
    private void AddEqualsOverloads(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record).EnsureNotNull<ISymbol, ITypeSymbol>();
        const string methodName = "Equals";
        
        context.WriteNewLine();
        context.WriteComment($"Equals(object)");
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
        [
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Isinst.WithOperand(recordTypeDefinitionVariable),
            OpCodes.Callvirt.WithOperand(_recordTypeEqualsOverloadMethodVar),
            OpCodes.Ret
        ];
        
        var bodyExps = CecilDefinitionsFactory.MethodBody(context.Naming, methodName, equalsObjectOverloadMethodVar, [], equalsBodyInstructions);
        context.WriteCecilExpressions(bodyExps);
        context.WriteCecilExpression($"{recordTypeDefinitionVariable}.Methods.Add({equalsObjectOverloadMethodVar});");

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
    
    private static string TypeEqualityOperator(IVisitorContext context)
    {
        var typeEqualityOperator = context.RoslynTypeSystem.SystemType.GetMembers("op_Equality")
            .OfType<IMethodSymbol>()
            .Single(Has2SystemTypeParameters).MethodResolverExpression(context);
        
        return typeEqualityOperator;
        
        bool Has2SystemTypeParameters(IMethodSymbol candidate) => 
            candidate.Parameters.Length == 2
            && SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, candidate.Parameters[1].Type);
    }
}
