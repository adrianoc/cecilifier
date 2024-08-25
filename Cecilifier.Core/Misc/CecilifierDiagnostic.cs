using System;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc;

public enum DiagnosticKind
{
    Information,
    Warning,
    Error
}

public record struct SourceLineInformation(int StartLineNumber, int StartColumn, int EndLineNumber, int EndColumn);

public record struct CecilifierDiagnostic(DiagnosticKind Kind, string Message, SourceLineInformation LineInformation)
{
    public static CecilifierDiagnostic FromCompiler(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan(); 
        return new CecilifierDiagnostic(
            diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => DiagnosticKind.Error,
                DiagnosticSeverity.Hidden => DiagnosticKind.Information,
                DiagnosticSeverity.Info => DiagnosticKind.Information,
                DiagnosticSeverity.Warning => DiagnosticKind.Warning,
                _ => throw new ArgumentOutOfRangeException()
            }, 
            diagnostic.GetMessage(),
            new SourceLineInformation(lineSpan.Span.Start.Line + 1, lineSpan.Span.Start.Character + 1, lineSpan.Span.End.Line + 1,lineSpan.Span.End.Character + 1));
    }
    
    public static CecilifierDiagnostic FromAstNode(SyntaxNode node, DiagnosticKind diagnosticKind, string message)
    {
        var lineSpan = node != null ? node.GetLocation().GetLineSpan() : new FileLinePositionSpan();
        return new CecilifierDiagnostic(
            diagnosticKind, 
            message,
            new SourceLineInformation(lineSpan.Span.Start.Line + 1, lineSpan.Span.Start.Character + 1, lineSpan.Span.End.Line + 1,lineSpan.Span.End.Character + 1));
    }
}
