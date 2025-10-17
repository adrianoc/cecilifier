using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
public class PropertyTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [Test]
    public void TestProperty()
    {
        AssertOutput("var c = new C(); c.Prop = 42; System.Console.Write(c.Prop); class C { public int Prop { get; set; } }", "42");
    }
}
