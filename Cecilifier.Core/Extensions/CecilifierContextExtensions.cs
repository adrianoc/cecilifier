using System;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;

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
}
