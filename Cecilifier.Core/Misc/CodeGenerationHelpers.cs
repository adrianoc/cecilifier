using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.Misc;

public struct CodeGenerationHelpers
{
    //TODO: Introduce method in IILGeneratorApiDriver
    internal static DefinitionVariable StoreTopOfStackInLocalVariable(IVisitorContext context, string ilVar, string variableName, ITypeSymbol type)
    {
        var methodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        var resolvedVarType = context.TypeResolver.ResolveAny(type);
        var tempLocalDefinitionVariable = context.AddLocalVariableToMethod(variableName, methodVar, resolvedVarType);
        context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Stloc, tempLocalDefinitionVariable.VariableName);
        return tempLocalDefinitionVariable;
    }
}
