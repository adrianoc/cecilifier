#nullable enable
using System.Diagnostics;

namespace Cecilifier.Core.ApiDriver;

[DebuggerDisplay("MemberDefinitionContext(Name:{Name}, Variable:{DefinitionVariable}, ParentVariable: {ParentDefinitionVariable})")]
public readonly record struct MemberDefinitionContext(string Name, string? NameAsValidIdentifier, string DefinitionVariable, string? ParentDefinitionVariable)
{
    public MemberDefinitionContext(string name, string definitionVariable, string? parentDefinitionVariable) : this(name, null, definitionVariable, parentDefinitionVariable)
    {
    }
    
    /// <summary>
    /// The equivalent of the <see cref="Name"/> of the member with the guarantee it is safe to be used as an identifier in C# code.
    /// </summary>
    public string Identifier => NameAsValidIdentifier ?? Name;

    public string ContainingTypeName { get; init; } = string.Empty;
}
