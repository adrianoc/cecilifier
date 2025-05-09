using System.Collections.Generic;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture]
public class ParamsTests : OutputBasedTestBase
{
    [TestCaseSource(nameof(ParamsTestScenarios))]
    public void Test(string paramsType, string args)
    {
        AssertOutput($$"""
                       using System;
                       M({{args}});
                       void M(params {{paramsType}} items) { foreach(var item in items) Console.Write($"{item},"); }
                       """, "1,2,3,", "ReturnPtrToStack");
    }

    private static IEnumerable<TestCaseData> ParamsTestScenarios()
    {
        string[] paramsTypes = ["int[]", "Span<int>", "ReadOnlySpan<int>"];
        foreach (var paramsType in paramsTypes)
        {
            yield return new TestCaseData(paramsType, "1, 2, 3").SetName($"Expanded - {paramsType}");
            yield return new TestCaseData(paramsType, "new[] { 1, 2, 3 }").SetName($"Non-Expanded - {paramsType}");
        }
    }
}

