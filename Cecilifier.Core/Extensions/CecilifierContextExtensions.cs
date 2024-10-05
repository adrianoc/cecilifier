using System;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions;

public static class CecilifierContextExtensions
{
    internal static void AddCompilerGeneratedAttributeTo(this IVisitorContext context, string memberVariable)
    {
        var compilerGeneratedAttributeCtor = context.RoslynTypeSystem.SystemRuntimeCompilerServicesCompilerGeneratedAttribute.Ctor();
        var exps = CecilDefinitionsFactory.Attribute("compilerGenerated", memberVariable, context, compilerGeneratedAttributeCtor.MethodResolverExpression(context));
        context.WriteCecilExpressions(exps);
    }
    
    internal static DefinitionVariable AddLocalVariableToCurrentMethod(this IVisitorContext context, string localVarName, string varType)
    {
        var currentMethod = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        if (!currentMethod.IsValid)
            throw new InvalidOperationException("Could not resolve current method declaration variable.");

        return AddLocalVariableToMethod(context, localVarName, currentMethod, varType);
    }

    internal static DefinitionVariable AddLocalVariableToMethod(this IVisitorContext context, string localVarName, DefinitionVariable methodVar, string resolvedVarType)
    {
        var cecilVarDeclName = context.Naming.SyntheticVariable(localVarName, ElementKind.LocalVariable);

        context.WriteCecilExpression($"var {cecilVarDeclName} = new VariableDefinition({resolvedVarType});");
        context.WriteNewLine();
        context.WriteCecilExpression($"{methodVar.VariableName}.Body.Variables.Add({cecilVarDeclName});");
        context.WriteNewLine();

        return context.DefinitionVariables.RegisterNonMethod(string.Empty, localVarName, VariableMemberKind.LocalVariable, cecilVarDeclName);
    }

    internal static bool TryApplyNumericConversion(this IVisitorContext context, string ilVar, ITypeSymbol source, ITypeSymbol target)
    {
        switch (target.SpecialType)
        {
            case SpecialType.System_Single:
                context.EmitCilInstruction(ilVar, OpCodes.Conv_R4);
                break;
            case SpecialType.System_Double:
                context.EmitCilInstruction(ilVar, OpCodes.Conv_R8);
                break;
            case SpecialType.System_Byte:
                context.EmitCilInstruction(ilVar, OpCodes.Conv_I1);
                break;
            case SpecialType.System_Int16:
                context.EmitCilInstruction(ilVar, OpCodes.Conv_I2);
                break;
            case SpecialType.System_Int32:
                // byte/char are pushed as Int32 by the runtime 
                if (source.SpecialType != SpecialType.System_SByte && source.SpecialType != SpecialType.System_Byte && source.SpecialType != SpecialType.System_Char)
                    context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
                break;
            case SpecialType.System_Int64:
                var convOpCode = source.SpecialType == SpecialType.System_Char || source.SpecialType == SpecialType.System_Byte ? OpCodes.Conv_U8 : OpCodes.Conv_I8;
                context.EmitCilInstruction(ilVar, convOpCode);
                break;
            case SpecialType.System_Decimal:
                var operand = target.GetMembers().OfType<IMethodSymbol>()
                    .Single(m => m.MethodKind == MethodKind.Constructor && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == source.SpecialType);
                context.EmitCilInstruction(ilVar, OpCodes.Newobj, operand.MethodResolverExpression(context));
                break;
            
            default: return false;
        }

        return true;
    }

    internal static void AddCallToMethod(this IVisitorContext context, IMethodSymbol method, string ilVar, MethodDispatchInformation dispatchInformation = MethodDispatchInformation.MostLikelyVirtual)
    {
        var needsVirtualDispatch = (method.IsVirtual || method.IsAbstract || method.IsOverride) && !method.ContainingType.IsPrimitiveType();

        var opCode = !method.IsStatic && dispatchInformation != MethodDispatchInformation.NonVirtual && (dispatchInformation != MethodDispatchInformation.MostLikelyNonVirtual || needsVirtualDispatch) &&
                     (method.ContainingType.TypeKind == TypeKind.TypeParameter || !method.ContainingType.IsValueType || needsVirtualDispatch)
            ? OpCodes.Callvirt
            : OpCodes.Call;

        EnsureForwardedMethod(context, method);

        var operand = method.MethodResolverExpression(context);
        if (context.TryGetFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint, out var constrainedType))
        {
            context.EmitCilInstruction(ilVar, OpCodes.Constrained, constrainedType);
            context.ClearFlag(Constants.ContextFlags.MemberReferenceRequiresConstraint);
        }

        context.EmitCilInstruction(ilVar, opCode, operand);
    }

    /*
     * Ensure forward member references are correctly handled, i.e, support for scenario in which a method is being referenced
     * before it has been declared. This can happen for instance in code like:
     *
     * class C
     * {
     *     void Foo() { Bar(); }
     *     void Bar() {}
     * }
     *
     * In this case when the first reference to Bar() is found (in method Foo()) the method itself has not been defined yet
     * so we add a MethodDefinition for it but *no body*. Method body will be processed later, when the method is visited.
     */
    internal static void EnsureForwardedMethod(this IVisitorContext context, IMethodSymbol method)
    {
        if (!method.IsDefinedInCurrentAssembly(context)) 
            return;

        var found = context.DefinitionVariables.GetMethodVariable(method.AsMethodDefinitionVariable());
        if (found.IsValid) 
            return;

        string methodDeclarationVar;
        var methodName = method.Name;
        if (method.MethodKind == MethodKind.LocalFunction)
        {
            methodDeclarationVar = context.Naming.SyntheticVariable(method.Name, ElementKind.Method);
            methodName = $"<{method.ContainingSymbol.Name}>g__{method.Name}|0_0";
        }
        else
        {
            methodDeclarationVar = method.MethodKind == MethodKind.Constructor
                ? context.Naming.Constructor((BaseTypeDeclarationSyntax) method.ContainingType.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(), method.IsStatic)
                : context.Naming.MethodDeclaration((BaseMethodDeclarationSyntax) method.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax());
        }

        var exps = CecilDefinitionsFactory.Method(context, methodDeclarationVar, methodName, "MethodAttributes.Private", method.ReturnType, method.ReturnsByRef, method.GetTypeParameterSyntax());
        context.WriteCecilExpressions(exps);

        foreach (var parameter in method.Parameters)
        {
            var paramVar = context.Naming.Parameter(parameter.Name);
            var parameterExps = CecilDefinitionsFactory.Parameter(context, parameter, methodDeclarationVar, paramVar);
            context.WriteCecilExpressions(parameterExps);
            context.DefinitionVariables.RegisterNonMethod(method.ToDisplayString(), parameter.Name, VariableMemberKind.Parameter, paramVar);
        }

        context.DefinitionVariables.RegisterMethod(method.AsMethodDefinitionVariable(methodDeclarationVar));
    }
}
