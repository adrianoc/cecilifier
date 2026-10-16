using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc;

public partial class CSharpCompilerDiagnosticParser
{
    public static IReadOnlyList<string> UnresolvedTypeNamesFrom(ImmutableArray<Diagnostic> diagnostics)
    {
        var unresolvedTypes = diagnostics.Where(d => d.Id == "CS0246").Select(GetTypeNameFromDiagnostic).ToList();
        return unresolvedTypes;
    }

    private static string GetTypeNameFromDiagnostic(Diagnostic arg)
    {
        var match = UnresolvedType().Match(arg.GetMessage());
        Debug.Assert(match.Success);
        
        return match.Groups[1].Value;
    }
    
    //The type or namespace name 'Foo' could not be found (are you missing a using directive or an assembly reference?)
    [GeneratedRegex(".+'(.+)'.+")]
    private static partial Regex UnresolvedType();
}
