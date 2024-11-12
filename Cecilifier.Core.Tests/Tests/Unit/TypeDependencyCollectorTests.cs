using System.Linq;
using Cecilifier.Core.TypeDependency;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class TypeDependencyCollectorTests
{
    [Test]
    public void Single_SyntaxTree()
    {
        var comp = CompilationFor("class Foo : Bar { } class Bar {}");
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Unordered.ToString(), Is.EqualTo("Foo,Bar"));
        Assert.That(collector.Ordered.ToString(), Is.EqualTo("Bar,Foo"));
    }

    [Test]
    public void Two_SyntaxTree()
    {
        var comp = CompilationFor("class Foo : Bar { }", "class Bar {}");
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Unordered.ToString(), Is.EqualTo("Foo,Bar"));
        Assert.That(collector.Ordered.ToString(), Is.EqualTo("Bar,Foo"));
    }

    [TestCase("class A : B {} class B {}", "B,A", TestName = "As base type")]
    [TestCase("class A { B b; } class B {}", "B,A", TestName = "As field")]
    [TestCase("class A { B b { get; set; } } class B {}", "B,A", TestName = "As property")]
    [TestCase("class A { event B b; } class B {}", "B,A", TestName = "As event as field")]
    [TestCase("class A { event B b { add {} remove {} } } class B {}", "B,A", TestName = "As event full")]
    [TestCase("class A { void M(B b) {} } class B {}", "B,A", TestName = "As parameter")]
    [TestCase("class A { void M() { B b; } } class B {}", "B,A", TestName = "As local variable")]
    [TestCase("class A { B M() => null; } class B {}", "B,A", TestName = "As return type")]
    [TestCase("class A { string M() => nameof(B); } class B {}", "B,A", TestName = "As name of")]
    [TestCase("class A { Type M() => typeof(B); class B {}", "B,A", TestName = "As type of")]
    [TestCase("class A { void M(object o) { object b = (B) o; } class B {}", "B,A", TestName = "As cast target")]
    [TestCase("class A { void M(object o) { object b = o as B; } class B {}", "B,A", TestName = "As *as* operator")]
    [TestCase("class A { bool M(object o) => is B; } class B {}", "B,A", TestName = "As *is* operator")]
    [TestCase("class A<T> { A<B> a; } class B {}", "B,A", TestName = "As type argument")]
    [TestCase("class A<T> where T: B { } class B {}", "B,A", TestName = "As type constraint")]
    [TestCase("class A : System.Collections.Generic.List<B> { } class B {}", "B,A", TestName = "As type argument")]
    [TestCase("class A { object M() => new B(); } class B {}", "B,A", TestName = "In object creation")]
    [TestCase("class A { object M() => new B[0]; } class B {}", "B,A", TestName = "In Array")]
    [TestCase("class A { object M() => B.M(); } class B { public static M() {} }", "B,A", TestName = "In static member reference")]
    [TestCase("class A { B<int> b; } class B<T> {}", "B,A", TestName = "Closed generic type")]
    [TestCase("class A<TParent> { B<TParent> b; } class B<T> {}", "B,A", TestName = "Open generic type")]
    [TestCase("[A] class B {} class AAttribute : System.Attribute {}", "AAttribute,B", TestName = "Attribute short syntax")]
    [TestCase("[AAttribute] class B {} class AAttribute : System.Attribute {}", "AAttribute,B", TestName = "Attribute long syntax")]
    [TestCase("[A<int>] class B {} class AAttribute<T> : System.Attribute {}", "AAttribute,B", TestName = "Attribute (generic) short syntax")]
    public void In_Various_Nodes(string code, string expected)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);
        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expected));
    }

    [TestCase("class A:B {} class B {}", "B,A", TestName = "Direct dependency")]
    [TestCase("class A:B {} class B:C {} class C {}", "C,B,A", TestName = "Three level direct dependency")]
    [TestCase("class A:B {} class B {} class C:B {}", "B,A,C", TestName = "Common dependency")]
    [TestCase("class A:B {} class B { A a; }", "A,B", TestName = "Cyclic dependency")]
    [TestCase("class A:B {} class B:C {} class C:A {}", "C,B,A", TestName = "Three level cyclic dependency")]
    [TestCase("class A { } class B {}", "A,B", TestName = "No dependency")]
    public void References_Levels(string code, string expected)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expected));
    }

    [TestCase("class A { NS.B b; } namespace NS { class B {} }", "B,A", TestName = "Qualified namespace")]
    [TestCase("using NS; class A { B b; } namespace NS { class B {} }", "B,A", TestName = "Unqualified namespace")]
    public void TypeSyntax_Types(string code, string expected)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expected));
    }

    [TestCase("class A : B  {} class B {}", "B,A", TestName = "Class")]
    [TestCase("class A { B b; } struct B {}", "B,A", TestName = "Struct")]
    [TestCase("class A { IFoo foo; } interface IFoo {}", "IFoo,A", TestName = "Interfaces")]
    [TestCase("class A { E e; } enum E {}", "E,A", TestName = "Enum")]
    public void KindOfType(string code, string expected)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expected));
    }

    [TestCase("class A : B {} class B : C {} class C {}", "C,B,A")]
    [TestCase("class A : B {} class B {} class C : B {}", "B,A,C")]
    [TestCase("class A : C {} class B : A {} class C {}", "C,A,B")]
    public void MultipleDependencyLevels(string code, string expectedOrder)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expectedOrder));
    }

    [TestCase("class A { public void M(B b) => b.M(); } class B { public void M(A a) => a.M(); }", "A,B")]
    [TestCase("class A : B { } class B : C { } class C { A a; }", "C,B,A")]
    public void CircularDependency(string code, string expectedOrder)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expectedOrder));
    }

    [TestCase("class A : B,C {} class B : C {} class C {}", "C,B,A")]
    [TestCase("class A : B,C {} class B {} class C {}", "B,C,A")]
    [TestCase("class A : C,B {} class B {} class C {}", "B,C,A")]
    [TestCase("class A : C,B {} class B : C {} class C {}", "C,B,A")]
    public void MultipleDependencies(string code, string expectedOrder)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expectedOrder));
    }

    [TestCase("class A:B { B b1; B b2; } class B { A a; }", "B,A")]
    [TestCase("class A:B { B b1; void M(B b) { } } class B { A a; }", "B,A", TestName = "In Parameters")]
    [TestCase("class A:B { B b1; B M() => this; } class B { A a; }", "B,A", TestName = "In Return")]
    public void NumberOfMemberReference_IsTakenIntoAccount_UponDependencyCycles(string code, string expectedOrder)
    {
        var comp = CompilationFor(code);
        var collector = new TypeDependencyCollector(comp);

        Assert.That(collector.Ordered.ToString(), Is.EqualTo(expectedOrder));
    }

    static CSharpCompilation CompilationFor(params string[] code)
    {
        var syntaxTrees = code.Select(source => CSharpSyntaxTree.ParseText(source));
        var compilation = CSharpCompilation.Create("Test", syntaxTrees);
        return compilation;
    }
}
