using System.Collections.Generic;
using System.Linq;

namespace Cecilifier.Core.Variables;

public class DefinitionVariableManager
{
    private readonly List<DefinitionVariable> _definitionStack = new List<DefinitionVariable>();

    private readonly List<DefinitionVariable> _definitionVariables = new List<DefinitionVariable>();

    public MethodDefinitionVariable RegisterMethod(string parentName, string methodName, string[] parameterTypes, string definitionVariableName)
    {
        var definitionVariable = new MethodDefinitionVariable(parentName, methodName, parameterTypes, definitionVariableName);
        _definitionVariables.Add(definitionVariable);

        return definitionVariable;
    }

    public DefinitionVariable RegisterNonMethod(string parentName, string memberName, VariableMemberKind variableMemberKind, string definitionVariableName)
    {
        var definitionVariable = new DefinitionVariable(parentName, memberName, variableMemberKind, definitionVariableName);
        _definitionVariables.Add(definitionVariable);

        return definitionVariable;
    }

    public DefinitionVariable GetMethodVariable(MethodDefinitionVariable tbf)
    {
        var methodVars = _definitionVariables.OfType<MethodDefinitionVariable>().Reverse();
        foreach (var candidate in methodVars)
        {
            if (candidate.Equals(tbf))
            {
                return candidate;
            }
        }

        return DefinitionVariable.NotFound;
    }

    public DefinitionVariable GetVariable(string memberName, VariableMemberKind variableMemberKind, string parentName = null)
    {
        var tbf = new DefinitionVariable(parentName ?? string.Empty, memberName, variableMemberKind);
        for (var i = _definitionVariables.Count - 1; i >= 0; i--)
        {
            if (_definitionVariables[i].Equals(tbf))
            {
                return _definitionVariables[i];
            }
        }

        return DefinitionVariable.NotFound;
    }

    public DefinitionVariable GetLastOf(VariableMemberKind kind)
    {
        var index = _definitionStack.FindLastIndex(c => c.Kind == kind);
        return index switch
        {
            -1 => DefinitionVariable.NotFound,
            _ => _definitionStack[index]
        };
    }

    public ScopedDefinitionVariable WithCurrentMethod(string parentName, string memberName, string[] paramTypes, string definitionVariableName)
    {
        var registered = RegisterMethod(parentName, memberName, paramTypes, definitionVariableName);
        _definitionStack.Add(registered);
        return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count - 1);
    }

    public ScopedDefinitionVariable WithCurrent(string parentName, string memberName, VariableMemberKind variableMemberKind, string definitionVariableName)
    {
        var registered = RegisterNonMethod(parentName, memberName, variableMemberKind, definitionVariableName);
        _definitionStack.Add(registered);
        return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count - 1);
    }

    public ScopedDefinitionVariable EnterScope()
    {
        return new ScopedDefinitionVariable(_definitionVariables, _definitionVariables.Count, true);
    }
}
