#nullable enable
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.ApiDriver;

public enum ExceptionHandlerKind
{
    Catch,
    Finally,
    Fault,
}

public record struct ExceptionHandlerEntry(ExceptionHandlerKind Kind, ResolvedType CatchType, string TryStart, string TryEnd, string HandlerStart, string HandlerEnd);

