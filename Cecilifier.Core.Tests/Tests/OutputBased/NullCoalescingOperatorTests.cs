using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

public class NullCoalescingOperatorTests(IILGeneratorApiDriver driver) : OutputBasedTestBase(driver)
{
    [Test]
    public void SimpleNullableValueType()
    {
        AssertOutput("""
                     System.Console.Write(M(42, 1));
                     int? M(int? i1, int? i2) => i1 ?? i2;
                     """, 
            "42");
    }
    
    [Test]
    public void SimpleReferenceType()
    {
        AssertOutput("""
                     System.Console.Write(M(null, "42"));
                     object M(object o1, object o2) => o1 ?? o2;
                     """, 
            "42");
    }
    
    [Test]
    public void MixedNullableValueType_AndReferenceType()
    {
        AssertOutput("""
                     var r = M3(42, 1);
                     System.Console.Write(r.Value);
                     
                     int? M3(int? i1, object i2) => i1 ?? (int) i2;
                     """, 
            "42");
    }
    
    [TestCase("(int?) o1 ?? (int?) o2")]
    [TestCase("(int?) o1 ?? (int) o2")]
    public void Convoluted(string coalescingExpression)
    {
        AssertOutput($"""
                     var r = M(42, 1);
                     System.Console.Write(r.Value);
                     
                     int? M(object o1, object o2) => {coalescingExpression};
                     """, 
            "42");
    }
    
    [TestCase(null, null,  "C", "C")]
    [TestCase(null, "B",  null, "B")]
    [TestCase(null, "B",  "C", "B")]
    [TestCase("A", "B",  "C", "A")]
    [TestCase("A", "B",  null, "A")]
    public void TestAssociative(string a, string b, string c, string expectedOutput)
    {
        AssertOutput($"System.Console.Write({Quote(a)} ?? {Quote(b)} ?? {Quote(c)});", expectedOutput);
        
        string Quote(string s) => s == null ? "null" : $"\"{s}\"";
    }
}
