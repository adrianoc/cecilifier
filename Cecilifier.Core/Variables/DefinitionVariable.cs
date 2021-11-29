using System;

namespace Cecilifier.Core.Variables;

public class DefinitionVariable : IEquatable<DefinitionVariable>
{
    public static readonly DefinitionVariable NotFound = new DefinitionVariable(string.Empty, string.Empty, default) {IsValid = false};

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
    private string ParentName { get; }

    public bool Equals(DefinitionVariable other)
    {
        return string.Equals(MemberName, other.MemberName)
               && string.Equals(ParentName, other.ParentName)
               && Kind == other.Kind;
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
        if (ParentName == null)
        {
            return $"(Kind = {Kind}) {MemberName}";
        }

        return $"(Kind = {Kind}) {ParentName}.{MemberName}";
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
