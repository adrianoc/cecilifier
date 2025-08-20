using System.Collections.Generic;
using System.IO;
using Cecilifier.Core.AST;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;

namespace Cecilifier.Core;

public readonly record struct CecilifierResult(StringReader GeneratedCode, string MainTypeName, IList<Mapping> Mappings, IVisitorContext Context, IList<CecilifierDiagnostic> diagnostics = null)
{
    public IList<CecilifierDiagnostic> Diagnostics { get; } = diagnostics ?? [];
}
