using System;
using System.Diagnostics.CodeAnalysis;

namespace Cecilifier.Core.Variables;

[ExcludeFromCodeCoverage]
public class MethodDefinitionVariable : DefinitionVariable, IEquatable<MethodDefinitionVariable>
{
    public static readonly MethodDefinitionVariable MethodNotFound = new MethodDefinitionVariable(string.Empty, string.Empty, [], 0);
    
    public MethodDefinitionVariable(string parentTypeName, string methodName, string[] parameterTypeNames, int typeParameterCountCount, string variableName = null) 
        : this(VariableMemberKind.Method, parentTypeName, methodName, parameterTypeNames, typeParameterCountCount, variableName)
    {
    }
    
    public MethodDefinitionVariable(VariableMemberKind methodKind, string parentTypeName, string methodName, string[] parameterTypeNames, int typeParameterCountCount, string variableName = null) 
        : base(parentTypeName, methodName, methodKind, variableName)
    {
        Parameters = parameterTypeNames;
        TypeParameterCount = typeParameterCountCount;
    }
    
    public MethodDefinitionVariable WithVariableName(string variableName) => new(Kind, ParentName, MemberName, Parameters, TypeParameterCount, variableName);
    
    private string[] Parameters { get; }
    
    private int TypeParameterCount { get; }

    public bool Equals(MethodDefinitionVariable other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!base.Equals(other))
        {
            return false;
        }

        if (Parameters.Length != other.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < Parameters.Length; i++)
        {
            if (Parameters[i] != other.Parameters[i])
            {
                return false;
            }
        }

        return TypeParameterCount == other.TypeParameterCount;
    }

    public static bool operator ==(MethodDefinitionVariable lhs, MethodDefinitionVariable rhs)
    {
        if (ReferenceEquals(lhs, null))
            return ReferenceEquals(rhs, null);
        
        return lhs.Equals(rhs);
    }

    public static bool operator !=(MethodDefinitionVariable lhs, MethodDefinitionVariable rhs)
    {
        return !(lhs == rhs);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((MethodDefinitionVariable) obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (base.GetHashCode() * 397) 
                   ^ (Parameters != null ? Parameters.GetHashCode() : 0)
                   ^ TypeParameterCount.GetHashCode();
        }
    }

    public override string ToString() => $"Method: {MemberName}({Parameters.Length}) => {VariableName}";
}
