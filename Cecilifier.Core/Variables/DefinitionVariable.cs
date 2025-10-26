using System;
using System.Collections.Generic;

namespace Cecilifier.Core.Variables;

public class DefinitionVariable : IEquatable<DefinitionVariable>
{
    public static readonly DefinitionVariable NotFound = new(string.Empty, string.Empty, default) { IsValid = false };

    public DefinitionVariable(string parentTypeName, string memberName, VariableMemberKind kind, string variableName = null)
    {
        ParentName = parentTypeName;
        MemberName = memberName;
        Kind = kind;
        VariableName = variableName;
        IsValid = true;
    }

    public string MemberName { get; }
    public VariableMemberKind Kind { get; }
    public string VariableName { get; }
    public bool IsValid { get; private set; }
    public bool IsForwarded { get; internal set; }

    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

    public string ParentName { get; }

    public bool Equals(DefinitionVariable other)
    {
        return string.Equals(MemberName, other.MemberName)
               && string.Equals(ParentName, other.ParentName)
               && (Kind & other.Kind) == Kind;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = MemberName != null ? MemberName.GetHashCode() : 0;
            hashCode = (hashCode * 397) ^ (ParentName != null ? ParentName.GetHashCode() : 0);
            hashCode = (hashCode * 397) ^ (int) Kind;
            return hashCode;
        }
    }

    public override string ToString()
    {
        return ParentName == null
            ? $"(Kind = {Kind}) {MemberName}"
            : $"(Kind = {Kind}) {ParentName}.{MemberName}";
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        return obj is DefinitionVariable other && Equals(other);
    }

    public static implicit operator string(DefinitionVariable variable)
    {
        return variable.VariableName;
    }
}
