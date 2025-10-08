using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
[EnableForContext<SystemReflectionMetadataContext>(nameof(Simple), IgnoreReason = "Not implemented yet")]
public class FieldTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
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

    [TestCase("int", "42", "42", TestName = "int")]
    [TestCase("string", "\"foo\"", "foo", TestName = "string")]
    public void Simple(string fieldType, string fieldValue, string expectedOutput)
    {
        AssertOutput($$"""
                       class Foo 
                       { 
                            public static {{fieldType}} field;
                            public static void Main()
                            {
                                Foo.field = {{ fieldValue }};
                                System.Console.WriteLine(Foo.field);
                            } 
                       }
                       """, 
            expectedOutput);
        
    }
}
