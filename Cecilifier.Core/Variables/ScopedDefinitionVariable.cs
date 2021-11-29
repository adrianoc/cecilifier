using System;
using System.Collections.Generic;

namespace Cecilifier.Core.Variables;

public unsafe struct ScopedDefinitionVariable : IDisposable
{
    private readonly List<DefinitionVariable> _definitionVariables;
    private readonly int _currentSize;
    private delegate*<IList<DefinitionVariable>, int, void> _unregister;

    public ScopedDefinitionVariable(List<DefinitionVariable> definitionVariables, int currentSize, bool dontUnregisterTypesAndMembers = false)
    {
        _definitionVariables = definitionVariables;
        _currentSize = currentSize;
        _unregister = dontUnregisterTypesAndMembers ? &ConditionalUnregister : &UnconditionalUnregister; 
    }

    public void Dispose()
    {
        for (int i = _definitionVariables.Count - 1; i >=  _currentSize; i--)
        {
            _unregister(_definitionVariables, i);
        }
    }
        
    static void ConditionalUnregister(IList<DefinitionVariable> variables, int index)
    {
        var v = variables[index];
        if (v.Kind == VariableMemberKind.LocalVariable || v.Kind == VariableMemberKind.Parameter || v.Kind == VariableMemberKind.TypeParameter) 
            variables.RemoveAt(index);
    }
            
    static void UnconditionalUnregister(IList<DefinitionVariable> variables, int index) => variables.RemoveAt(index);
}
