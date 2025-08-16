using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions;

public sealed class VariableDefinitionComparer : IEqualityComparer<VariableDefinition>
{
    private static readonly Lazy<IEqualityComparer<VariableDefinition>> instance = new(() => new VariableDefinitionComparer());

    public static IEqualityComparer<VariableDefinition> Instance => instance.Value;

    public bool Equals(VariableDefinition x, VariableDefinition y)
    {
        if (x == null && y == null)
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        return x.Index == y.Index && x.VariableType.FullName == y.VariableType.FullName;
    }

    public int GetHashCode(VariableDefinition obj)
    {
        return obj.Index.GetHashCode() + 37 * obj.VariableType.FullName.GetHashCode();
    }
}
