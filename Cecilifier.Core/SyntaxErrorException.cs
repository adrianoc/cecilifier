using System;
using System.Linq;
using Cecilifier.Core.Misc;

namespace Cecilifier.Core
{
    public class SyntaxErrorException : Exception
    {
        public CompilationError[] Errors { get; }

        public SyntaxErrorException(CompilationError[] errors)
        {
            Errors = errors;
        }

        public override string ToString()
        {
            return string.Join('\n', Errors.Select(err => err.Message));
        }
    }
}
