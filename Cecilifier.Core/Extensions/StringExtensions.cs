using System;

namespace Cecilifier.Core.Extensions
{
    public static class StringExtensions
    {
        public static int CountNewLines(this string value) => value.AsSpan().Count('\n');
    }
}
