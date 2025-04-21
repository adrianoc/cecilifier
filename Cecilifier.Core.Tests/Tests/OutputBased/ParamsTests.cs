using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture]
public class ParamsTests : OutputBasedTestBase
{
    [TestCase("1, 2, 3", TestName = "Expanded")]
    [TestCase("new[] { 1, 2, 3 }", TestName = "Non-Expanded")]
    public void Test(string args)
    {
        AssertOutput($$"""
                       using System;
                       M({{args}});
                       void M(params int[] items) { foreach(var item in items) Console.Write($"{item},"); }
                       """, "1,2,3,");
    }
}
