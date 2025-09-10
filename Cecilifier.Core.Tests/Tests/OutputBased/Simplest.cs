using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
public class Simplest<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [Test]
    public void SimpleCallToConsoleWriteLine()
    {
        AssertOutput("""
                     public class Foo
                     {
                        public static void Main()
                        {
                            System.Console.Write("Hello World!");
                        }
                     }
                     """, "Hello World!");
    }
}
