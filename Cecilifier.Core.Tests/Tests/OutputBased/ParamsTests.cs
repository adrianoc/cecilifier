using System.Collections.Generic;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
[EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
public class ParamsTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [TestCaseSource(nameof(ParamsTestScenarios))]
    public void TestNonNullables(string paramsType, string args)
    {
        AssertOutput($$"""
                       using System;
                       using System.Collections.Generic;
                       M({{args}});
                       void M(params {{paramsType}} items) { foreach(var item in items) Console.Write($"{item},"); }
                       """, "1,2,3,", "ReturnPtrToStack");
    }
    
    private static IEnumerable<TestCaseData> ParamsTestScenarios()
    {
        string[] paramsTypes = ["int[]", "Span<int>", "ReadOnlySpan<int>", "IList<int>", "ICollection<int>"];
        foreach (var paramsType in paramsTypes)
        {
            yield return new TestCaseData(paramsType, "1, 2, 3").SetName($"Expanded - {paramsType}");
            yield return new TestCaseData(paramsType, "new[] { 1, 2, 3 }").SetName($"Non-Expanded - {paramsType}");
        }
    }
}

