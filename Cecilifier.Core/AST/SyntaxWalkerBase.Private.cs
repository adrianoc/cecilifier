using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.AST;

// Helper types / members useful only to SyntaxWalkerBase.
// Some of these may be refactored and made public/internal if needed in the future.
internal partial class SyntaxWalkerBase
{
    internal enum AttributeKind
    {
        DllImport,
        StructLayout,
        Ordinary
    }
}

public static class PrivateExtensions
{
    internal static SyntaxWalkerBase.AttributeKind AttributeKind(this ITypeSymbol self) => (self.ContainingNamespace.ToString(), self.Name) switch
    {
        ("System.Runtime.InteropServices", "DllImportAttribute") => SyntaxWalkerBase.AttributeKind.DllImport,
        ("System.Runtime.InteropServices", "StructLayoutAttribute") => SyntaxWalkerBase.AttributeKind.StructLayout,
        _ => SyntaxWalkerBase.AttributeKind.Ordinary,
    };
}

