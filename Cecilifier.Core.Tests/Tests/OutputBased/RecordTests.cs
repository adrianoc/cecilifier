using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

public class RecordTests
{
    [TestFixture(typeof(MonoCecilContext), TestName = "Mono.Cecil")]
    [TestFixture(typeof(SystemReflectionMetadataContext),  TestName = "System.Reflection.Metadata")]
    [EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
    public class Misc<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
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
                """
                System.Console.WriteLine(new D(42, true, "42")); 

                record D(int Value, bool IsCool, string Str):B1(IsCool, Str); 
                record B1(bool IsCool, string Str) : B2(Str);
                record B2(string Str);
                """, 
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
    
        [Test]
        public void RecordPropertyAccess()
        {
            AssertOutput(
                """
                var ri = new Record(42);
                System.Console.WriteLine(ri.Value); 

                record Record(int Value);
                """,
                "42");
        }        
        
        [Test]
        public void CyclicReferences()
        {
            AssertOutput(
                """
                var ri = new Record("Child", new Record("Parent", null));
                System.Console.WriteLine($"{ri.Name},{ri.Other.Equals(ri.Other)},{ri}"); 

                record Record(string Name, Record Other);
                """,
                "Child,True,Record { Name = Child, Other = Record { Name = Parent, Other =  } }");
        }
    }

    [TestFixture(typeof(MonoCecilContext), TestName = "Mono.Cecil")]
    [TestFixture(typeof(SystemReflectionMetadataContext),  TestName = "System.Reflection.Metadata")]
    [EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
    public class EqualsOverloadTest<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
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

    [TestFixture(typeof(MonoCecilContext), TestName = "Mono.Cecil")]
    [TestFixture(typeof(SystemReflectionMetadataContext),  TestName = "System.Reflection.Metadata")]
    [EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
    public class Deconstruct<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
    {
        [Test]
        public void Deconstruct_WhenInheritingFromObject_IncludesAllPrimaryConstructorParameters()
        {
            AssertOutput(
                """
                var r = new Record(42, "Foo");
                r.Deconstruct(out var i, out var s); // Cecilifier does not support deconstructing syntax so we just call the Deconstruct() method manually. 
                
                System.Console.WriteLine($"{i},{s}"); 
                
                record Record(int Value, string Name);
                """,
                "42,Foo");
        }
        
        [Test]
        public void Deconstruct_WhenInheritingFromRecord_IncludesAllPrimaryConstructorParameters()
        {
            AssertOutput(
                """
                var r = new Derived2(42, "Foo", true);
                r.Deconstruct(out var i, out var s, out var b); // Cecilifier does not support deconstructing syntax so we just call the Deconstruct() method manually. 
                
                System.Console.WriteLine($"{i},{s},{b}"); 
                
                record Base(string Name);
                record Derived(int Value, string Name) : Base(Name);
                record Derived2(int Value, string Name, bool IsCool) : Derived(Value, Name);
                """,
                "42,Foo,True");
        }
        
        [TestCase("class", true, TestName = "Class")]
        [TestCase("", true, TestName = "Class (implicit)")]
        public void CopyConstructor_WhenInheritingFromObject_Works(string kind, bool copyCtorExpected)
        {
            AssertOutput($$"""
                               System.Console.WriteLine(new Foo(42).Duplicate());
                               
                               record {{kind}} Foo(int Value)
                               {
                                  public Foo Duplicate() => new Foo(this);
                               }
                               """,
                "Foo { Value = 42 }");
        }
        
        [TestCase("class", true, TestName = "Class")]
        [TestCase("", true, TestName = "Class (implicit)")]
        public void CopyConstructor_WhenInheritingFromRecord_Works(string kind, bool copyCtorExpected)
        {
            AssertOutput($$"""
                               System.Console.WriteLine(new Derived(42, "Foo").Duplicate());
                               
                               record {{kind}} Base(int Value);
                               
                               record {{kind}} Derived(int Value, string Name) : Base(Value)
                               {
                                  public Derived Duplicate() => new Derived(this);
                               }
                               """,
                "Derived { Value = 42, Name = Foo }");
        }
    }

    [TestFixture(typeof(MonoCecilContext), TestName = "Mono.Cecil")]
    [TestFixture(typeof(SystemReflectionMetadataContext),  TestName = "System.Reflection.Metadata")]
    [EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
    public class Generic<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
    {
        [Test]
        public void SimpleGenericRecord()
        {
            AssertOutput(
                """
                var ri = new Record<int>(42);
                System.Console.WriteLine($"{ri},{ri.Value},{ri.GetHashCode() != 0},{ri.Equals(ri)},{ri == ri},{ri != ri}");

                record Record<T>(T Value);
                """,
                "Record { Value = 42 },42,True,True,True,False");
        }
        
        [Test]
        public void Deconstruct()
        {
            AssertOutput(
                """
                var gr = new Record<int>(42);
                gr.Deconstruct(out var i); // Cecilifier does not support deconstructing syntax so we just call the Deconstruct() method manually.
                System.Console.WriteLine($"{i}");

                record Record<T>(T Value);
                """,
                "42");
        }
    
        [Test]
        public void PrintMembers_Includes_PublicFields([Values] bool isGeneric)
        {
            AssertOutput($$"""
                         System.Console.Write(new Record{{(isGeneric ? "<bool>": "")}}(42) { Name = "Foo" });
                         public record Record{{(isGeneric ? "<T>": "")}}(int Value) { public string Name; }
                         """,
                "Record { Value = 42, Name = Foo }");
        }
    }
    
    [TestFixture(typeof(MonoCecilContext))]
    [TestFixture(typeof(SystemReflectionMetadataContext))]
    [EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
    public class RecordStructs<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
    {
        [Test]
        public void Test()
        {
            AssertOutput("""
                         var d = new RecordStruct(42, "Foo");
                         System.Console.WriteLine(d);
                          
                         record struct RecordStruct(int IntValue, string StringValue); 
                         """,
                "RecordStruct { IntValue = 42, StringValue = Foo }");
        }
    }
}
