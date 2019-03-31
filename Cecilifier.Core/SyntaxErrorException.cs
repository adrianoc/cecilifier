using System;

namespace Cecilifier.Core
{
    public class SyntaxErrorException : Exception
    {
        public SyntaxErrorException(string message) : base(message)
        {
        }
    }
}
