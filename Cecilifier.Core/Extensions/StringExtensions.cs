using System;
using System.Collections.Generic;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Extensions
{
    public static class StringExtensions
    {
        public static int CountNewLines(this string value) => value.AsSpan().Count('\n');
    }
}
