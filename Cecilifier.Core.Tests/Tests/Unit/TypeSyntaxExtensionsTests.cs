using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class TypeSyntaxExtensionsTests
{

    [TestCase("class C { int underTest; }", "int", TestName = nameof(PredefinedTypeSyntax))]
    [TestCase("unsafe class C { int* underTest; }", "int", TestName = "Predefined Pointer Type")]
    [TestCase("class C { int[] underTest; }", "int", TestName = nameof(ArrayTypeSyntax))]
    [TestCase("class C<T> { T underTest; }", "T", TestName = nameof(TypeParameterSyntax))]
    [TestCase("class C { System.Action underTest; }", "System.Action", TestName = nameof(QualifiedNameSyntax))]
    [TestCase("class C { int? underTest; }", "int", TestName = nameof(NullableTypeSyntax))]
    [TestCase("class C { (int, string) underTest; }", "(int, string)", TestName = nameof(TupleTypeSyntax))]
    [TestCase("ref struct C { ref System.Span<int> underTest; }", "System.Span<int>", TestName = nameof(RefTypeSyntax))]
    [TestCase("unsafe class C { delegate*<int,void> underTest; }", "delegate*<int,void>", TestName = nameof(FunctionPointerTypeSyntax))]
    [TestCase("using Foo=System; class C { Foo::String underTest; }", "Foo::String", TestName = nameof(AliasQualifiedNameSyntax))]
    [TestCase("using System; class C { Action<int> underTest; }", "Action", TestName = nameof(GenericNameSyntax))]
    public void TestForCommonNodes(string code, string expected)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var fields = syntaxTree.GetRoot().DescendantNodes();
        var field = syntaxTree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single();
        
        var name = field.Declaration.Type.NameFrom();
        Assert.That(name, Is.EqualTo(expected));
    }

    [Test]
    public void OmittedTypeArgumentSyntaxTest()
    {
        var testCode = "var t = typeof(System.Action<>);";
        var syntaxTree = CSharpSyntaxTree.ParseText(testCode);
        var position = testCode.IndexOf("<>");
        var type = syntaxTree.GetRoot().FindToken(position).Parent.DescendantNodes().OfType<OmittedTypeArgumentSyntax>().Single();
        
        Assert.That(type.NameFrom(), Is.EqualTo("Action<>"));
    }
}
