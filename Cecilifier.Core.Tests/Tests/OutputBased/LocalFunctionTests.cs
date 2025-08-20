using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
[EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
public class LocalFunctionTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [TestCase("static", TestName = "Static")]
    [TestCase("", TestName = "Instance")]
    public void InstanceLocalFunction(string staticOrInstance)
    {
        AssertOutput($"""
                     System.Console.Write(M(1));
                     {staticOrInstance} int M(int i) => 41 + i;
                     """,
            "42");
    }
}
