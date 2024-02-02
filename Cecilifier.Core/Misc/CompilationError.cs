using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc;

public record struct CompilationError(int StartLineNumber, int StartColumn, int EndLineNumber, int EndColumn, string Message)
{
    public static implicit operator CompilationError(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan(); return new CompilationError(
            lineSpan.Span.Start.Line + 1, 
            lineSpan.Span.Start.Character + 1,
            lineSpan.Span.End.Line + 1, 
            lineSpan.Span.End.Character + 1,
            diagnostic.GetMessage());
    }
}
