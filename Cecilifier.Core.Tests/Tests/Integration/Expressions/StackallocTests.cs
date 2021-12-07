using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration;

[TestFixture]
public class StackallocTests : IntegrationResourceBasedTest
{
    
    [Test]
    public void TestStackalloc()
    {
        AssertResourceTest(@"Expressions/Stackalloc/simplest");
    }
    
    [Test]
    public void TestStackallocWithSpan()
    {
        AssertResourceTest(@"Expressions/Stackalloc/WithSpan");
    }
}
