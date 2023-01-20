using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class InterfaceTests : CecilifierUnitTestBase
{
    [TestCase(
        "using System; class Foo : IDisposable { void IDisposable.Dispose() {} }",
        "m_dispose_\\d+.Overrides.Add\\(.+System.IDisposable.+, \"Dispose\".+\\);",
        TestName = "From external assembly")]
    [TestCase(
        "interface IFoo<T> where T : IFoo<T> { void M(); }  class Foo : IFoo<Foo> { void IFoo<Foo>.M() {} }",
            """
                        var (m_M_\d+) = new MethodDefinition\("IFoo<Foo>.M",.+\);
                        \s+(cls_foo_\d+).Methods.Add\(\1\);
                        \s+m_M_\d+.Overrides.Add\(new MethodReference\(m_M_2.Name, m_M_2.ReturnType\) {.+DeclaringType = itf_iFoo_\d+.MakeGenericInstanceType\(\2\),.+\);
                        """,
        TestName = "Generics")]
    [TestCase(
        "interface IFoo { int P { get; set; } } class Foo : IFoo { int IFoo.P { get => 42; set {} } }",
            """
                        (m_get_\d+).Overrides.Add\(m_get_\d+\);
                        \s+cls_foo_5.Methods.Add\(\1\);
                        """,
        TestName = "Property")]
    public void ExplicitInterfaceImplementation_SetsOverrideProperty(string source, string expectedRegex)
    {
        var result = RunCecilifier(source);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedRegex));
    }
}
