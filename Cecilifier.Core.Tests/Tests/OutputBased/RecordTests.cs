using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutbutBased;

[TestFixture]
public class RecordTests : OutputBasedTestBase
{
    [Test]
    public void ToString_WhenInheritFromObjectWithSingleProperty_ReturnsProperty()
    {
        var output = CecilifyAndExecute("System.Console.WriteLine(new R(42)); record R(int Value);");
        Assert.That(output, Is.EqualTo("R { Value = 42 }"));
    }
    
    [Test]
    public void ToString_WhenInheritFromObjectWithMultipleProperties_ReturnsProperties()
    {
        var output = CecilifyAndExecute("System.Console.WriteLine(new R(42, true)); record R(int Value, bool IsCool);");
        
        Assert.That(output, Is.EqualTo("R { Value = 42, IsCool = True }"));
    }
}
