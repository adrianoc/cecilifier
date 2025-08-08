using System.Collections.Generic;
using System.IO;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;

namespace Cecilifier.Core;

public struct CecilifierResult
{
    public CecilifierResult(StringReader generatedCode, string mainTypeName, IList<Mapping> mappings, IList<CecilifierDiagnostic> diagnostics = null)
    {
        GeneratedCode = generatedCode;
        MainTypeName = mainTypeName;
        Mappings = mappings;
        Diagnostics = diagnostics ?? [];
    }

    public StringReader GeneratedCode { get; }
    public string MainTypeName { get; }
    public IList<Mapping> Mappings { get; }
    public IList<CecilifierDiagnostic> Diagnostics { get; }
}
