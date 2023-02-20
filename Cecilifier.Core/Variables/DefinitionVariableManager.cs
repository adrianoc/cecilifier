using System.Collections.Generic;
using System.Linq;

namespace Cecilifier.Core.Variables;

public class DefinitionVariableManager
{
    private readonly List<DefinitionVariable> _definitionStack = new();

    private readonly List<DefinitionVariable> _definitionVariables = new();

    public MethodDefinitionVariable RegisterMethod(MethodDefinitionVariable variable)
    {
        _definitionVariables.Add(variable);
        return variable;
    }

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
        return WithVariable(registered);
    }

    public ScopedDefinitionVariable WithVariable(DefinitionVariable variable)
    {
        _definitionStack.Add(variable);
        return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count - 1);
    }

    public ScopedDefinitionVariable WithCurrent(string parentName, string memberName, VariableMemberKind variableMemberKind, string definitionVariableName)
    {
        var found = GetVariable(memberName, variableMemberKind, parentName);
        var registered = found.IsValid
            ? found
            : RegisterNonMethod(parentName, memberName, variableMemberKind, definitionVariableName);

        _definitionStack.Add(registered);
        return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count - 1);
    }

    /// <summary>
    /// Ensures that any scope specific variable definition registered after its creation will be removed at disposal.
    /// </summary>
    /// <remarks>
    /// Entering in such scope is useful to avoid cluttering the available variable definitions which could lead
    /// to potential false positives during lookups in scenarios with two or more local variables in different
    /// methods symbols with the same name. For example, failure to remove registered variables for the following
    /// source snippet would cause runtime errors due to the code  generated for `Baz()` referencing `local` from `Bar()` 
    /// <example>
    /// class Foo
    /// {
    ///     void Bar()
    ///     {
    ///        int local = 10;
    ///     }
    /// 
    ///     void Baz()
    ///     {
    ///         int local_i;
    ///         string local = "local"; // without cleaning registered variables after visiting `Bar()` this
    ///                                 // would either introduce a duplicated key exception or a run time
    ///                                 // exception.  
    ///     }
    /// }
    /// </example>
    /// </remarks>
    /// <returns>
    /// a new ScopedDefinitionVariable that, when disposed, will remove all local variables
    /// from the list of defined variables.</returns>
    public ScopedDefinitionVariable EnterLocalScope()
    {
        return new ScopedDefinitionVariable(_definitionVariables, _definitionVariables.Count, true);
    }
}
