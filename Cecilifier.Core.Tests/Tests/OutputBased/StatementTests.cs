using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
public class StatementTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [Test]
    public void SimpleCallToConsoleWriteLine()
    {
        AssertOutput("""
                     if (args.Length == 0) System.Console.Write("No Arguments");
                     if (args.Length > 0)
                     {
                        System.Console.Write("This should never be executed");
                     }
                     else
                     {
                        System.Console.Write("|@Else");
                     }    
                     """, "No Arguments|@Else");
    }
}
