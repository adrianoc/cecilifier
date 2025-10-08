#nullable enable
using System.Diagnostics;
using Cecilifier.Core.AST;

namespace Cecilifier.Core.ApiDriver;

/// <summary>
/// Defines a context used to define members with bodies, for instance methods, properties, events, etc.
/// </summary>
[DebuggerDisplay("MemberDefinitionContext ({MemberDefinitionVariableName}, {ParentDefinitionVariableName}, {IlContext})")]
public record struct BodiedMemberDefinitionContext(string MemberDefinitionVariableName, string? ParentDefinitionVariableName, MemberOptions Options, IlContext IlContext);
