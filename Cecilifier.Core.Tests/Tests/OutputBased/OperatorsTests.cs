using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
public class OperatorsTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [Test]
    public void PrePostIncrement_OnRefLikeLocations_Works()
    {
        AssertOutput("""
                     int v = 2;
                     DoIt(ref v);
                     
                     
                     void DoIt(ref int x)
                     {
                        int n = ++x;
                        System.Console.Write($"{n} ");
                        
                        n = x--;
                        System.Console.Write($"{n} {x} ");

                        ref int r = ref x;
                        n = --r;
                        System.Console.Write($"{n} {x}");
                     }
                     """, 
            "3 3 2 1 1");
    }
}
