using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutbutBased;

[TestFixture]
public class RecordTests : OutputBasedTestBase
{
    [Test]
    public void ToString_WhenInheritFromObjectWithSingleProperty_ReturnsProperty()
    {
        AssertOutput("System.Console.WriteLine(new R(42)); record R(int Value);", "R { Value = 42 }");
    }
    
    [Test]
    public void ToString_WhenInheritFromObjectWithMultipleProperties_ReturnsProperties()
    {
        AssertOutput("System.Console.WriteLine(new R(42, true)); record R(int Value, bool IsCool);", "R { Value = 42, IsCool = True }");
    }
    
    [Test]
    public void ToString_WhenInheritFromRecord_IncludesBaseRecordProperties()
    {
        AssertOutput(
            "System.Console.WriteLine(new Derived(42, true)); record Derived(int Value, bool IsCool):Base(IsCool); record Base(bool IsCool);", 
            "Derived { IsCool = True, Value = 42 }");
    }
    
    [Test]
    public void ToString_WhenInheritFromRecord_IncludesBaseRecordProperties2()
    {
        AssertOutput(
            """System.Console.WriteLine(new D(42, true, "42")); record D(int Value, bool IsCool, string Str):B1(IsCool, Str); record B1(bool IsCool, string Str) : B2(Str); record B2(string Str);""", 
            "D { Str = 42, IsCool = True, Value = 42 }");
    }
    
    [Test]
    public void Constructor_WhenInheritFromRecordWithProperties_CorrectArgumentsArePassed()
    {
        AssertOutput("""
                     var d = new Derived(42, "Foo");
                     System.Console.WriteLine("end");
                      
                     record Derived(int IntValue, string StringValue):Base(StringValue); 
                     record Base(string StringValue);
                     """,
            "end");
    }
}
