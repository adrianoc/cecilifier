using System;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Misc;

public struct CodeGenerationHelpers
{
    internal static DefinitionVariable StoreTopOfStackInLocalVariable(IVisitorContext context, string ilVar, string variableName, ITypeSymbol type)
    {
        var methodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        var resolvedVarType = context.TypeResolver.Resolve(type);
        var tempLocalName = AddLocalVariableWithResolvedType(context, variableName, methodVar, resolvedVarType);
        context.EmitCilInstruction(ilVar, OpCodes.Stloc, tempLocalName.VariableName);
        return tempLocalName;
    }
        
    internal static DefinitionVariable AddLocalVariableWithResolvedType(IVisitorContext context, string localVarName, DefinitionVariable methodVar, string resolvedVarType)
    {
        var cecilVarDeclName = context.Naming.SyntheticVariable(localVarName, ElementKind.LocalVariable);

        context.WriteCecilExpression($"var {cecilVarDeclName} = new VariableDefinition({resolvedVarType});");
        context.WriteNewLine();
        context.WriteCecilExpression($"{methodVar.VariableName}.Body.Variables.Add({cecilVarDeclName});");
        context.WriteNewLine();

        return context.DefinitionVariables.RegisterNonMethod(string.Empty, localVarName, VariableMemberKind.LocalVariable, cecilVarDeclName);
    }
    
    internal static DefinitionVariable AddLocalVariableToCurrentMethod(IVisitorContext context, string localVarName, string varType)
    {
        var currentMethod = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        if (!currentMethod.IsValid)
            throw new InvalidOperationException("Could not resolve current method declaration variable.");

        return AddLocalVariableWithResolvedType(context, localVarName, currentMethod, varType);
    }       
}
