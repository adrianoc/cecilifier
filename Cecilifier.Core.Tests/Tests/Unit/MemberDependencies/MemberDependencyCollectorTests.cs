using System.Linq;
using Cecilifier.Core.AST.MemberDependencies;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit.MemberDependencies;

[TestFixture]
public class MemberDependencyCollectorTests : MemberDependencyTestBase
{
    [TestCase("Method()", TestName = "Method")]
    [TestCase("Event()", TestName = "Event")]
    [TestCase("Property", TestName = "Property")]
    [TestCase("Field", TestName = "Field")]
    [TestCase("new Test() + new Test()", "+",  TestName = "Operators")]
    [TestCase("new Test()", "+",  TestName = "ImplicitOperators")]
    [TestCase("((string) new Test()).Length", "string",  TestName = "ExplicitOperators")]
    public void SimpleDirectMemberDependency(string targetMemberReference, string expectedMemberReference = null)
    {
        var comp = CompilationFor($$"""
                                                               class Test
                                                               {
                                                                    int M1() => {{targetMemberReference}};
                                                                    
                                                                    int Method() => 42;
                                                                    event System.Func<int> Event;
                                                                    int Property {get; set;}
                                                                    int Field;
                                                                    public static int operator+(Test lhs, Test rhs) => 1;
                                                                    public static implicit operator int(Test t) => 1;
                                                                    public static explicit operator string(Test t) => "Foo";
                                                               }
                                                               """);
        
        var computedDependencies = CollectDependenciesFromSingleType(comp);
        Assert.That(computedDependencies.Count, Is.EqualTo(8));
        
        var m1 = computedDependencies.ElementAt(0);
        Assert.That(m1.Declaration, Is.TypeOf<MethodDeclarationSyntax>());
        Assert.That(((MethodDeclarationSyntax) m1.Declaration).Identifier.Text, Is.EqualTo("M1"));

        if (TestContext.CurrentContext.Test.Name == "ImplicitOperators")
        {
            // We don´t collect dependencies for implicit operators.. it would take too long to analyze all expressions inside 
            // method bodies
            return;
        }
        Assert.That(m1.Dependencies.Count, Is.EqualTo(1));
        var m1Dependency = m1.Dependencies[0];
        var dependeeName = MemberNameFrom(m1Dependency);
        
        Assert.That(dependeeName, Is.EqualTo(expectedMemberReference ?? TestContext.CurrentContext.Test.Name));
        Assert.That(m1Dependency.Dependencies.Count, Is.EqualTo(0));
    }

    [Test]
    public void Constructor()
    {
        var comp = CompilationFor($$"""
                                    class Test
                                    {
                                         public Test() : this(42) {}
                                         public Test(int value) {}
                                         
                                         public static Test Create() => new Test();
                                    }
                                    """);
        
        var computedDependencies = CollectDependenciesFromSingleType(comp);
        Assert.That(computedDependencies.Count, Is.EqualTo(3));
        
        var parameterlessCtor = computedDependencies.ElementAt(0);
        Assert.That(parameterlessCtor.Declaration, Is.TypeOf<ConstructorDeclarationSyntax>());
        Assert.That(((ConstructorDeclarationSyntax) parameterlessCtor.Declaration).Identifier.Text, Is.EqualTo("Test"));
        
        Assert.That(parameterlessCtor.Dependencies.Count, Is.EqualTo(1));
        Assert.That(parameterlessCtor.Dependencies[0].Declaration.ToString(), Is.EqualTo("public Test(int value) {}"));
    }
    
    [Test]
    public void PropertyGetDependencies()
    {
        var comp = CompilationFor("""
                                  class Test
                                  {
                                       int TestProperty 
                                       {
                                          get 
                                          {
                                              return Method() + Event() + Field + Property;
                                          }
                                       }
                                       
                                       int Method() => 42;
                                       event System.Func<int> Event;
                                       int Property {get; set;}
                                       int Field;
                                  }
                                  """);
        
       
        var computedDependencies = CollectDependenciesFromSingleType(comp);
        
        Assert.That(computedDependencies.Count, Is.EqualTo(5));
        var testProperty = computedDependencies.ElementAt(0);
        
        Assert.That(testProperty.Declaration, Is.TypeOf<PropertyDeclarationSyntax>());
        Assert.That(((PropertyDeclarationSyntax) testProperty.Declaration).Identifier.Text, Is.EqualTo("TestProperty"));
        
        Assert.That(testProperty.Dependencies.Count, Is.EqualTo(4));
        var dependees = testProperty.Dependencies.Select(MemberNameFrom);
        string[] dependeeNames = ["Event", "Method", "Field", "Property"];
        Assert.That(dependees, Is.EquivalentTo(dependeeNames));
    }
    
    [Test]
    public void Indexers()
    {
        var comp = CompilationFor("""
                                  class Test
                                  {
                                     int this[string s] => this[s.Length];
                                     int this[int i] => i;
                                  }
                                  """);
        
       
        var computedDependencies = CollectDependenciesFromSingleType(comp);
        
        Assert.That(computedDependencies.Count, Is.EqualTo(2));
        var testIndexer = computedDependencies.ElementAt(0);
        
        Assert.That(testIndexer.Declaration, Is.TypeOf<IndexerDeclarationSyntax>());
        Assert.That(testIndexer.Declaration.ToString(), Is.EqualTo("int this[string s] => this[s.Length];"));
        
        Assert.That(testIndexer.Dependencies.Count, Is.EqualTo(1));
        Assert.That(testIndexer.Dependencies[0].Declaration.ToString(), Is.EquivalentTo("int this[int i] => i;"));
    }
    
    [Test]
    public void OnlyMembers_OfTypeUnderTest_AreCollected()
    {
        var comp = CompilationFor("""
                                  class Test
                                  {
                                       int TestMethod(Test t, string s) 
                                       {
                                           return t.Method() + s.Length;
                                       }
                                       
                                       int Method() => 42;
                                  }
                                  """);
        
       
        var typeUnderTest = comp.SyntaxTrees[0].GetRoot().ChildNodes().OfType<TypeDeclarationSyntax>().Single();

        var collector = new MemberDependencyCollector<MemberDependency>();
        var computedDependencies = collector.Process(typeUnderTest, comp.GetSemanticModel(comp.SyntaxTrees[0]));
        
        Assert.That(computedDependencies.Count, Is.EqualTo(2));
        var testMethod = computedDependencies.ElementAt(0);
        
        Assert.That(testMethod.Declaration, Is.TypeOf<MethodDeclarationSyntax>());
        Assert.That(MemberNameFrom(testMethod), Is.EqualTo("TestMethod"));
        
        Assert.That(testMethod.Dependencies.Count, Is.EqualTo(1));
        Assert.That(MemberNameFrom(testMethod.Dependencies[0]), Is.EqualTo("Method"));
    }

    [Test]
    public void InnerTypes()
    {
        var comp = CompilationFor("""
                                  class Test
                                  {
                                       struct TestInnerStruct
                                       {
                                          public int value;
                                       }
                                       int M1() => M2();
                                       int M2() => new TestInnerStruct().value;
                                  }
                                  """);
        
       
        var outerTypeUnderTest = comp.SyntaxTrees[0].GetRoot().ChildNodes().OfType<TypeDeclarationSyntax>().Single(t => t.Identifier.Text == "Test");

        var collector = new MemberDependencyCollector<MemberDependency>();
        var computedDependencies = collector.Process(outerTypeUnderTest, comp.GetSemanticModel(comp.SyntaxTrees[0]));
        
        Assert.That(computedDependencies.Count, Is.EqualTo(2), string.Join(",", computedDependencies.Select(MemberNameFrom)));
        Assert.That(computedDependencies.Select(MemberNameFrom), Does.Not.Contain("value"));
    }
}
