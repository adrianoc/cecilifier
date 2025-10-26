using System;
using Cecilifier.Core.ApiDriver.Handles;

namespace Cecilifier.Core.Extensions
{
    public static class StringExtensions
    {
        public static int CountNewLines(this string value) => value.AsSpan().Count('\n');
        
        public static CilToken AsToken(this string value) => new(value);
    }
}
