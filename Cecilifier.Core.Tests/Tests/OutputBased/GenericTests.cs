using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture]
public class GenericTests : OutputBasedTestBase
{
    [Test]
    public void NonGenericTypedProperty_OnGenericType_Works()
    {
        AssertOutput("""
                     class Foo<T>
                     {
                         public int Value => 42;
                     }
                     
                     class Runner 
                     {
                        static void Main()
                        {
                            var f = new Foo<int>();
                            System.Console.WriteLine(f.Value);
                        }
                     }
                     """, 
            "42");
    }
}
