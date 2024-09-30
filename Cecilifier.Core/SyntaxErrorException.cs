using System;
using System.Linq;
using Cecilifier.Core.Misc;

namespace Cecilifier.Core
{
    public class SyntaxErrorException : Exception
    {
        public CecilifierDiagnostic[] Diagnostics { get; }

        public SyntaxErrorException(CecilifierDiagnostic[] diagnostics)
        {
            Diagnostics = diagnostics;
        }

        public override string Message => ToString();

        public override string ToString()
        {
            return string.Join('\n', Diagnostics.Select(err => err.Message));
        }
    }
}
