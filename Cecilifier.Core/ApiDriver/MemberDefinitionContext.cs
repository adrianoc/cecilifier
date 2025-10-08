#nullable enable
using System.Diagnostics;
using Cecilifier.Core.AST;

namespace Cecilifier.Core.ApiDriver;

[DebuggerDisplay("MemberDefinitionContext ({MemberDefinitionVariableName}, {ParentDefinitionVariableName}, {IlContext})")]
public record struct MemberDefinitionContext(string MemberDefinitionVariableName, string? ParentDefinitionVariableName, MemberOptions Options, IlContext IlContext);
