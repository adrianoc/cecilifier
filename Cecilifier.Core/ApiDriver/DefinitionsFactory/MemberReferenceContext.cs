#nullable enable
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.ApiDriver;

public readonly record struct MemberReferenceContext(in ResolvedType DeclaringType, string Name, MemberOptions Options, string DefinitionVariable);
