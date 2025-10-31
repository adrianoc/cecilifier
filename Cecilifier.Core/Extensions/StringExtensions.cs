using System;
using Cecilifier.Core.ApiDriver.Handles;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.Extensions
{
    public static class StringExtensions
    {
        public static int CountNewLines(this string value) => value.AsSpan().Count('\n');
        
        public static CilToken AsToken(this string value) => new(value);
        public static CilToken AsToken(this ResolvedType value) => new(value);
    }
}
