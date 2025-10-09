#nullable enable
using System.Diagnostics;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.ApiDriver;

/// <summary>
/// Defines a context used to define members with bodies, for instance methods, properties, events, etc.
/// </summary>
[DebuggerDisplay("BodiedMemberDefinitionContext({Member}, {Options}, {IlContext})")]
public record struct BodiedMemberDefinitionContext(MemberDefinitionContext Member, MemberOptions Options, IlContext IlContext)
{
    public BodiedMemberDefinitionContext(string name, string nameAsValidIdentifier, string definitionVariable, string? parentDefinitionVariable, MemberOptions options, IlContext ilContext) :
        this(new MemberDefinitionContext(name, nameAsValidIdentifier, definitionVariable, parentDefinitionVariable), options, ilContext)
    {
    }
    
    public BodiedMemberDefinitionContext(string name, string definitionVariable, string? parentDefinitionVariable, MemberOptions options, IlContext ilContext) :
        this(new MemberDefinitionContext(name, definitionVariable, parentDefinitionVariable), options, ilContext)
    {
    }
}

[DebuggerDisplay("MemberDefinitionContext({DefinitionVariable}, {ParentDefinitionVariable})")]
public record struct MemberDefinitionContext(string Name, string? NameAsValidIdentifier, string DefinitionVariable, string? ParentDefinitionVariable)
{
    public MemberDefinitionContext(string name, string definitionVariable, string? parentDefinitionVariable) : this(name, null, definitionVariable, parentDefinitionVariable)
    {
    }
    public string Identifier => NameAsValidIdentifier ?? Name;
}
