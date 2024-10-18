using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture]
public class FieldTests : OutputBasedTestBase
{
    [TestCase("int", "field = 42", "42",  TestName = "Non generic")]
    [TestCase("T", "field = true", "True", TestName = "Generic")]
    public void InstanceFieldOnGenericType(string fieldType, string fieldInitialization, string expectedOutput)
    {
        AssertOutput($$"""
                            var f = new Foo<bool>() { {{ fieldInitialization }} };
                            System.Console.WriteLine(f.field);
                            
                            class Foo<T> { public {{fieldType}} field; }
                            """, 
            expectedOutput);
    }
    
    [TestCase("int", "42", "42",  TestName = "Non generic")]
    [TestCase("T", "true", "True", TestName = "Generic")]
    public void StaticFieldOnGenericType(string fieldType, string fieldValue, string expectedOutput)
    {
        AssertOutput($$"""
                            Foo<bool>.field = {{ fieldValue }};
                            System.Console.WriteLine(Foo<bool>.field);
                            
                            class Foo<T> { public static {{fieldType}} field; }
                            """, 
            expectedOutput);
    }
}
