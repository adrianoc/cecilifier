using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture]
public class LocalFunctionTests : OutputBasedTestBase
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
