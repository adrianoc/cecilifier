using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
public class ArrayTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [Test]
    public void SimplestLocalArray() => AssertOutput("var a = new int[10]; a[0] = 42; System.Console.Write(a[0]);", "42");
}
