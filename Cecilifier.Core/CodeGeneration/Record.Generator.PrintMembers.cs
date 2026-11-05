using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

internal partial class RecordGenerator
{
    private string PrintMembersVar;
    
    private void AddPrintMembersMethod(string stringBuilderAppendStringMethod, INamedTypeSymbol stringBuilderSymbol)
    {
        const string PrintMembersMethodName = "PrintMembers";
        
        context.WriteNewLine();
        context.WriteComment($"{record.Identifier.ValueText}.{PrintMembersMethodName}()");

        //var printMembersMethodDefinitionVarName = context.Naming.SyntheticVariable(PrintMembersMethodName, ElementKind.Method);
        var builderParameter = new ParameterSpec("builder", context.TypeResolver.Resolve(context.RoslynTypeSystem.ForType<StringBuilder>(), ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None);
        string methodModifiers = $"MethodAttributes.Family | {(HasBaseRecord(record) ? Constants.Cecil.HideBySigVirtual : Constants.Cecil.HideBySigNewSlotVirtual)}";
        var ilContext = context.ApiDriver.NewIlContext(context, PrintMembersMethodName, context.Naming.SyntheticVariable(PrintMembersMethodName, ElementKind.Method));
        var printMembersDeclExps = context.ApiDefinitionsFactory.Method(
                                                                context, 
                                                                new BodiedMemberDefinitionContext(PrintMembersMethodName, ilContext.AssociatedMethodVariable, recordTypeDefinitionVariable, MemberOptions.None, ilContext), 
                                                                _recordSymbol.OriginalDefinition.ToDisplayString(), 
                                                                methodModifiers, 
                                                                new [] { builderParameter }, 
                                                                Array.Empty<string>(), 
                                                                BooleanReturnTypeResolver,
                                                                out var methodDefinitionVariable);

        // use the returned variable instead of `printMembersMethodDefinitionVarName` because some drivers (SRM) will return a variable for a method reference instead of a method definition.
        PrintMembersVar = methodDefinitionVariable.VariableName; 
        
        using var _ = context.DefinitionVariables.WithVariable(methodDefinitionVariable);
        context.Generate(printMembersDeclExps);

        List<InstructionRepresentation> bodyInstructions = HasBaseRecord(context, _recordSymbol)
            ?
            [
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Call.WithOperand(PrintMembersMethodToCall(stringBuilderSymbol).AsToken()),
                OpCodes.Brfalse_S.WithBranchOperand("DoNotAppendComma"),
                OpCodes.Ldarg_1,
                OpCodes.Ldstr.WithOperand("\", \""),
                OpCodes.Callvirt.WithOperand(stringBuilderAppendStringMethod.AsToken()),
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
                OpCodes.Callvirt.WithOperand(stringBuilderAppendStringMethod.AsToken()),
                OpCodes.Pop,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_0,
                OpCodes.Call.WithOperand(ClosedGenericMethodFor($"get_{property.Name}", recordTypeDefinitionVariable).AsToken()),
                OpCodes.Box.WithOperand(context.TypeResolver.Resolve(property.Type, ResolveTargetKind.None).Expression.AsToken()).IgnoreIf(property.Type.TypeKind != TypeKind.TypeParameter),
                OpCodes.Callvirt.WithOperand(stringBuilderAppendMethod.MethodResolverExpression(context).AsToken()),
                OpCodes.Pop
            ]);
            separator = ", ";
        }

        foreach (var field in _recordSymbol.GetMembers().OfType<IFieldSymbol>().Where(f => f.DeclaredAccessibility == Accessibility.Public))
        {
            var stringBuilderAppendMethod = StringBuilderAppendMethodFor(context, field.Type, stringBuilderSymbol);
            if (stringBuilderAppendMethod == null)
            {
                context.WriteComment($"Field '{field.Name}' of type {field.Type.Name} not supported in PrintMembers()/ToString() ");
                context.WriteComment("Only primitives, string and object are supported. The implementation of PrintMembers()/ToString() is definitely incomplete.");
                continue;
            }

            bodyInstructions.AddRange(
            [
                OpCodes.Ldarg_1,
                OpCodes.Ldstr.WithOperand($"\"{separator}{field.Name} = \""),
                OpCodes.Callvirt.WithOperand(stringBuilderAppendStringMethod.AsToken()),
                OpCodes.Pop,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_0,
                OpCodes.Ldfld.WithOperand($"""new FieldReference("{field.Name}", {context.TypeResolver.Resolve(field.Type, ResolveTargetKind.TypeReference)}, {TypeOrClosedTypeFor(recordTypeDefinitionVariable)})"""),
                OpCodes.Box.WithOperand(context.TypeResolver.Resolve(field.Type, ResolveTargetKind.Instruction).Expression.AsToken()).IgnoreIf(field.Type.TypeKind != TypeKind.TypeParameter),
                OpCodes.Callvirt.WithOperand(stringBuilderAppendMethod.MethodResolverExpression(context).AsToken()),
                OpCodes.Pop
            ]);
            separator = ", ";
        }

        InstructionRepresentation[] instructions = [
            ..bodyInstructions,
            OpCodes.Ldc_I4_1,
            OpCodes.Ret
        ];
        var printMemberBodyExps = context.ApiDefinitionsFactory.MethodBody(context, PrintMembersMethodName, ilContext, [], instructions);
        context.Generate(printMemberBodyExps);
        AddCompilerGeneratedAttributeTo(context, PrintMembersVar, VariableMemberKind.Method);
        AddIsReadOnlyAttributeTo(context, PrintMembersVar);
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
    
    // Returns a `MethodReference` for the PrintMembers() method to be invoked
    // If the record inherits from another record returns the base `PrintMembers()` method
    private string PrintMembersMethodToCall(INamedTypeSymbol stringBuilderSymbol)
    {
        if (_recordSymbol is { IsGenericType: true })
            return $$"""new MethodReference("PrintMembers", {{context.TypeResolver.Bcl.System.Boolean}}, {{context.TypeResolver.Resolve(_recordSymbol, ResolveTargetKind.None)}}) { HasThis = true, Parameters = { new ParameterDefinition({{context.TypeResolver.Resolve(stringBuilderSymbol, ResolveTargetKind.Parameter)}}) } }""";
            
        return HasBaseRecord(context, _recordSymbol) 
            ? $$"""new MethodReference("PrintMembers", {{context.TypeResolver.Bcl.System.Boolean}}, {{context.TypeResolver.Resolve(_recordSymbol.BaseType, ResolveTargetKind.None)}}) { HasThis = true, Parameters = { new ParameterDefinition({{ context.TypeResolver.Resolve(stringBuilderSymbol, ResolveTargetKind.Parameter) }}) } }"""
            : PrintMembersVar;
    }
}
