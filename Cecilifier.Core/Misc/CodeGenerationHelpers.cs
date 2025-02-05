using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
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
        var tempLocalDefinitionVariable = context.AddLocalVariableToMethod(variableName, methodVar, resolvedVarType);
        context.EmitCilInstruction(ilVar, OpCodes.Stloc, tempLocalDefinitionVariable.VariableName);
        return tempLocalDefinitionVariable;
    }
}
