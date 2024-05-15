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
    
    internal void AddSyntheticMembers(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        AddEqualityContractPropertyIfNeeded(context, recordTypeDefinitionVariable, record);
        PrimaryConstructorGenerator.AddPropertiesFrom(context, recordTypeDefinitionVariable, record);
        PrimaryConstructorGenerator.AddPrimaryConstructor(context, recordTypeDefinitionVariable, record);
        AddIEquatableEquals(context, recordTypeDefinitionVariable, record);
        AddToStringAndRelatedMethods(context, recordTypeDefinitionVariable, record);
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
                                                        $"MethodAttributes.Family | {Constants.Cecil.HideBySigNewSlotVirtual}",
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
                    OpCodes.Call.WithOperand($"new MethodReference(\"{PrintMembersMethodName}\", {context.TypeResolver.Bcl.System.Boolean}, {context.TypeResolver.Resolve(recordSymbol.BaseType)})"),
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
                OpCodes.Call.WithOperand(PrintMembersMethodToCall()),
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
                ? $"""new MethodReference("PrintMembers", {context.TypeResolver.Bcl.System.Boolean}, {context.TypeResolver.Resolve(recordSymbol.BaseType)})"""
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
        var equalsMethodVar = context.Naming.SyntheticVariable("Equals", ElementKind.Method);
        var exps = CecilDefinitionsFactory.Method(
                                    context,
                                    record.Identifier.ValueText(),
                                    equalsMethodVar,
                                    $"{record.Identifier.ValueText()}.Equals", "Equals",
                                    $"MethodAttributes.Public | MethodAttributes.HideBySig | {Constants.Cecil.InterfaceMethodDefinitionAttributes}", //TODO: No NEWSLOT if in derived record
                                    [new ParameterSpec("other", recordTypeDefinitionVariable, RefKind.None, Constants.ParameterAttributes.None)],
                                    Array.Empty<string>(),
                                    ctx => ctx.TypeResolver.Bcl.System.Boolean, out var methodDefinitionVariable);

        context.WriteCecilExpressions([..exps, $"{recordTypeDefinitionVariable}.Methods.Add({equalsMethodVar});"]);
        
        // Compare each unique primary constructor parameter to compute equality.
        using (context.DefinitionVariables.WithVariable(methodDefinitionVariable))
        {
            var uniqueParameters = record.GetUniqueParameters(context);
            var equalityDataByType = GenerateEqualityComparerMethods(context, uniqueParameters);
        
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
                instructions.Add(OpCodes.Call.WithOperand(equalityDataByType[parameterType.Name].GetDefaultMethodVar));
                
                // load property backing field for 'this' 
                var paramDefVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText()), VariableMemberKind.Field, record.Identifier.ValueText());
                instructions.Add(OpCodes.Ldarg_0);
                instructions.Add(OpCodes.Ldfld.WithOperand(paramDefVar.VariableName));
                
                // load property backing field for 'other' 
                instructions.Add(OpCodes.Ldarg_1);
                instructions.Add(OpCodes.Ldfld.WithOperand(paramDefVar.VariableName));
                
                // compares both backing fields.
                instructions.Add(OpCodes.Callvirt.WithOperand(equalityDataByType[parameterType.Name].EqualsMethodVar));
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
            var equalsExps = CecilDefinitionsFactory.MethodBody(context.Naming, "Equals",equalsMethodVar, ilVar, [], instructions.ToArray());
            context.WriteCecilExpressions(equalsExps);
        }
    }

    private IDictionary<string, (string GetDefaultMethodVar, string EqualsMethodVar)> GenerateEqualityComparerMethods(IVisitorContext context, IReadOnlyList<ParameterSyntax> uniqueParameters)
    {
        Dictionary<string, (string, string)> equalityComparerDataByType = new();
        
        foreach (var parameter in uniqueParameters)
        {
            var openEqualityComparerType = context.TypeResolver.Resolve(context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(EqualityComparer<>).FullName!));
            var parameterType = context.SemanticModel.GetTypeInfo(parameter.Type!).Type.EnsureNotNull();
            if (equalityComparerDataByType.ContainsKey(parameterType.Name))
                continue;
            
            var equalityComparerOfParameterType = openEqualityComparerType.MakeGenericInstanceType(context.TypeResolver.Resolve(parameterType));
            var openGetDefaultMethodVar = context.Naming.SyntheticVariable("openget_Default", ElementKind.LocalVariable);

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

            equalityComparerDataByType[parameterType.Name] = (getDefaultMethodVar, equalsMethodVar);
        }

        return equalityComparerDataByType;
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

    private static bool HasBaseRecord(TypeDeclarationSyntax record)
    {
        if (record.BaseList?.Types.Count is 0 or null)
            return false;
        
        var baseRecordName = ((IdentifierNameSyntax) record.BaseList!.Types.First().Type).Identifier;
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
