using System;
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
    }
}
