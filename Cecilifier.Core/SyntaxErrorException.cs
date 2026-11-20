using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cecilifier.Core.Misc;

namespace Cecilifier.Core
{
    [ExcludeFromCodeCoverage]
    public class SyntaxErrorException : Exception
    {
        public IList<CecilifierDiagnostic> Diagnostics { get; }
        public IReadOnlyList<string> UnresolvedTypes { get; }

        public SyntaxErrorException(IList<CecilifierDiagnostic> diagnostics, IReadOnlyList<string> unresolvedTypes)
        {
            Diagnostics = diagnostics;
            UnresolvedTypes = unresolvedTypes;
        }

        public override string Message => ToString();

        public override string ToString()
        {
            return string.Join('\n', Diagnostics.Select(err => err.Message));
        }
    }
}
