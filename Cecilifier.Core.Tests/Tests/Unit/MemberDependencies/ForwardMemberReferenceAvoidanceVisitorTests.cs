using System.Text;
using Cecilifier.Core.AST.MemberDependencies;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit.MemberDependencies;

[TestFixture]
public class MemberDependencyIteratorTest : MemberDependencyTestBase
{
    [TestCase("[System.Obsolete] class Foo {}", TestName = "Class")]
    [TestCase("[System.Obsolete] struct Foo {}", TestName = "Struct")]
    [TestCase("[System.Obsolete] record Foo {}", TestName = "Record")]
    public void TypeWithNoMembers(string code)
    {
        var comp = CompilationFor(code);
        AssertMemberOrder(comp, "");
    }
    
    [Test]
    public void SmokeTest()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                    void M() => M1();
                                    void M1() { M2(); M4(); }
                                    void M2() { M3(); }
                                    void M3() {}
                                    void M4() {}
                                    void M5() {}
                                  }
                                  """);

        AssertMemberOrder(comp, "M3,M2,M4,M1,M,M5,");
    }
    
    [Test]
    public void UnreferencedMethodsAreProcessed()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                    int M() => 1;
                                    int M2() => 2;
                                  }
                                  """);

        AssertMemberOrder(comp, "M,M2,");
    }
    
    [Test]
    public void DirectCircularDependency()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                    void M() => M1();
                                    void M1() => M();
                                  }
                                  """);

        AssertMemberOrder(comp, "M1,M,");
    }
    
    [Test]
    public void TransitiveCircularDependency()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                    void M() => M1();
                                    void M1() => M2();
                                    void M2() => M();
                                  }
                                  """);

        AssertMemberOrder(comp, "M2,M1,M,");
    }
    
    [Test]
    public void MethodDependingOnProperty()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                    int M() => P1;
                                    int P1 { get; set; } 
                                  }
                                  """);

        AssertMemberOrder(comp, "P1,M,");
    }
    
    [TestCase("{ get { return M(); } }", TestName = "Manual getter")]
    [TestCase("=> M();", TestName = "Expression bodied getter")]
    public void PropertyDependingOnMethod(string getterImpl)
    {
        var comp = CompilationFor($$"""
                                  class TestClass
                                  {
                                    int M() => 42;
                                    int P {{getterImpl}} 
                                  }
                                  """);

        AssertMemberOrder(comp, "M,P,");
    }
    
    [Test]
    public void Fields()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                    int M() => _field + P;
                                    int P => _field;
                                    int _field; 
                                  }
                                  """);

        AssertMemberOrder(comp, "_field,P,M,");
    }
    
    [Test]
    public void Events()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                     int M() => IntEvent();
                                     event System.Func<int> IntEvent;
                                  }
                                  """);
        AssertMemberOrder(comp, "IntEvent,M,");
    }
    
    [Test]
    public void Constructors()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                      public TestClass() : this(42) {}
                                      public TestClass(int i) {}
                                  }
                                  """);

        AssertMemberOrder(comp, "public TestClass(int i) {},public TestClass() : this(42) {},");
    }
    
    [Test]
    public void Indexers()
    {
        var comp = CompilationFor("""
                                  class TestClass
                                  {
                                      int this[string s] => this[s.Length];
                                      int this[int i] => i;
                                  }
                                  """);

        AssertMemberOrder(comp, "int this[int i] => i;,int this[string s] => this[s.Length];,");
    }

    [Test]
    public void Operator()
    {
        var comp = CompilationFor($$"""
                                    class Test
                                    {
                                         int M1() => new Test() + new Test();
                                         public static int operator+(Test lhs, Test rhs) => 1;
                                    }
                                    """);
        AssertMemberOrder(comp, "operator+,M1,");
    }
    
    [Test]
    public void ConversionOperator()
    {
        var comp = CompilationFor($$"""
                                    class Test
                                    {
                                         int M1() => (int) new Test();
                                         public static explicit operator int(Test lhs) => 1;
                                    }
                                    """);
        AssertMemberOrder(comp, "operator int(),M1,");
    }

    private void AssertMemberOrder(CSharpCompilation comp, string expected)
    {
        var computedDependencies = CollectDependenciesFromSingleType(comp);
        var memberWriter = new MemberVisitor();
        var visitor = new ForwardMemberReferenceAvoidanceVisitor(memberWriter);
        foreach (var dep in computedDependencies)
        {
            dep.Accept(visitor);
        }
        Assert.That(memberWriter.ToString(), Is.EqualTo(expected));
    }
}

internal class MemberVisitor : CSharpSyntaxVisitor
{
    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        _builder.Append($"{node.Identifier.ToString()},");
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        _builder.Append($"{node.Identifier.ToString()},");
    }

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        _builder.Append($"{node.Identifier.ToString()},");
    }

    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        _builder.Append($"{node.Declaration.Variables[0].Identifier.ToString()},");
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        _builder.Append($"{node.Identifier.ToString()},");
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        _builder.Append($"{node},");
    }

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        _builder.Append($"{node},");
    }

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        _builder.Append($"operator{node.OperatorToken.ToString()},");
    }

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        _builder.Append($"operator {node.Type}(),");
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        _builder.Append($"{node.Declaration.Variables[0].Identifier.ToString()},");
    }

    public override string ToString() => _builder.ToString();

    private StringBuilder _builder = new();
}
