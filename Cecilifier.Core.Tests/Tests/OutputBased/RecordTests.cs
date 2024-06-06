using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

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
    public void GetHashCode_WhenInheritFromObject_IsHandled()
    {
        AssertOutput(
            """System.Console.WriteLine(new Record(42, "Foo").GetHashCode() != 0); record Record(int Value, string Str);""",
            "True");
    }
    
    [Test]
    public void GetHashCode_WhenInheritFromRecord_IsHandled()
    {
        AssertOutput(
            """System.Console.WriteLine(new Derived(42, "Foo").GetHashCode() != 0); record Base(int Value); record Derived(int Value, string Str) : Base(Value);""",
            "True");
    }

    [TestFixture]
    public class Equals : OutputBasedTestBase
    {
        [Test]
        public void RecordTypeOverload_WhenInheritingFromObject_Works()
        {
            AssertOutput("""
                         var r1 = new Record(42, "Foo");
                         var r2 = new Record(42, "Foo");
                         var r3 = new Record(1, "Bar");
                         
                         System.Console.WriteLine($"{r1.Equals(r1)}/{r1.Equals(r2)}/{r1.Equals(r3)}");
                          
                         record Record(int Value, string Name);
                         """,
                "True/True/False");
        }
        
        [Test]
        public void ObjectOverload_WhenInheritingFromObject_Works()
        {
            AssertOutput("""
                         object r1 = new Record(42, "Foo");
                         object r2 = new Record(42, "Foo");
                         object r3 = new Record(1, "Bar");
                         
                         System.Console.WriteLine($"{r1.Equals(r1)}/{r1.Equals(r2)}/{r1.Equals(r3)}");
                          
                         record Record(int Value, string Name);
                         """,
                "True/True/False");
        }
        
        [Test]
        public void VariousOverloads_WhenInheritingFromRecord_Works()
        {
            AssertOutput("""
                         var r1 = new Derived(42, "Foo");
                         var r2 = new Derived(42, "Foo");
                         var r3 = new Derived(1, "Bar");
                         Base r1AsBase = r1;
                         
                         System.Console.WriteLine($"{r1.Equals(r1)}/{r1.Equals(r2)}/{r1.Equals(r3)}/{r1.Equals(r1AsBase)}");
                          
                         record Base(string Name);
                         record Derived(int Value, string Name) : Base(Name);
                         """,
                "True/True/False/True");
        }
        
        [TestCase("==", "True/True/False", TestName = "Equality")]
        [TestCase("!=", "False/False/True", TestName = "Inequality")]
        public void EqualityOperator_WhenInheritingFromObject_Works(string operatorToTest, string expectedResult)
        {
            AssertOutput($$"""
                         var r1 = new Record(42, "Foo");
                         var r2 = new Record(42, "Foo");
                         var r3 = new Record(1, "Bar");
                         
                         System.Console.WriteLine($"{r1 {{operatorToTest}} r1}/{r1 {{operatorToTest}} r2}/{r1 {{operatorToTest}} r3}");
                          
                         record Record(int Value, string Name);
                         """,
                expectedResult);
        }
        
        [Test]
        public void EqualityOperator_WhenInheritingFromRecord_Works()
        {
            AssertOutput("""
                         var r1 = new Derived(42, "Foo");
                         var r2 = new Derived(42, "Foo");
                         var r3 = new Derived(1, "Bar");
                         Base r1AsBase = r1;

                         System.Console.WriteLine($"{r1 == r1AsBase}");
                          
                         record Base(string Name);
                         record Derived(int Value, string Name) : Base(Name);
                         """,
                "True");
        }
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
