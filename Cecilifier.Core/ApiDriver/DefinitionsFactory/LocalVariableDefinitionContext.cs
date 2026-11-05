#nullable enable
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.ApiDriver;

public record struct LocalVariableDefinitionContext(string Name, ResolvedType Type, string MethodVariable);
