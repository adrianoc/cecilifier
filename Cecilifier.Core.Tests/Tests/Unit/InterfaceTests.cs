using System;
using System.Text.RegularExpressions;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class InterfaceTests : CecilifierUnitTestBase
{
    [TestCase("public interface IFoo<T> { abstract static T M(); }", "Void", TestName = "With Generic")]
    [TestCase("public interface IFoo { abstract static int M(); }", "Int32", TestName = "Simple")]
    public void AbstractStaticMethodDefinitionTest(string code, string expectedReturnTypeInDeclaration)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            $"""
            new MethodDefinition("M", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Abstract, assembly.MainModule.TypeSystem.{expectedReturnTypeInDeclaration});
            """));
    }

    [TestCase("public interface IFoo<T> { abstract static T M(); } class Foo : IFoo<Foo> { public static Foo M() => null; }", "cls_foo_4", TestName = "With Generic1")]
    [TestCase("public interface IFoo { abstract static int M(); } class Foo : IFoo { public static int M() => 0; }", "assembly.MainModule.TypeSystem.Int32", TestName = "Simple1")]
    public void AbstractStaticMethodImplementationTest(string code, string expectedReturnTypeInImplementation)
    {
        var result = RunCecilifier(code);
        var cecilified = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilified, Does.Match(
            $$"""
            \s+var (m_M_\d+) = new MethodDefinition\("M", MethodAttributes.Public \| MethodAttributes.Static \| MethodAttributes.HideBySig, {{expectedReturnTypeInImplementation}}\);
            \s+(cls_foo_\d+).Methods.Add\(\1\);
            """));

        var itfMethodDefVar = Regex.Match(cecilified, @"itf_iFoo_\d+.Methods.Add\((m_M_\d+)\);").Groups[1].Value;
        Assert.That(
            cecilified, Does.Match(
                $$"""
                m_M_\d+.Overrides.Add\({{itfMethodDefVar}}|new MethodReference\({{itfMethodDefVar}}.Name, {{itfMethodDefVar}}.ReturnType\) {.+DeclaringType = itf_iFoo_\d+.MakeGenericInstanceType\(cls_foo_\d+\).+}\);
                """));
    }

    [TestCase("public interface IFoo<T> { abstract static T M(); } class C  { T M<T>() where T : class, IFoo<T> { return T.M(); } }", "cls_foo_3", TestName = "With Generic")]
    public void AbstractStaticMethodReferenceTest(string code, string expectedReturnTypeInImplementation)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(
            """
            (il_M_\d+\.Emit\(OpCodes\.)Constrained, (gp_T_\d+)\);
            \s+\1Call, new MethodReference\(m_M_2.Name, m_M_2.ReturnType\) {  HasThis = m_M_2.HasThis, ExplicitThis = m_M_2.ExplicitThis, DeclaringType = itf_iFoo_0.MakeGenericInstanceType\(\2\), CallingConvention = m_M_2.CallingConvention,}\);
            """));
    }

    [TestCase(
        "using System; class Foo : IDisposable { void IDisposable.Dispose() {} }",
        "m_dispose_\\d+.Overrides.Add\\(.+System.IDisposable.+, \"Dispose\".+\\);",
        TestName = "From external assembly")]
    [TestCase(
        "interface IFoo<T> where T : IFoo<T> { void M(); }  class Foo : IFoo<Foo> { void IFoo<Foo>.M() {} }",
            """
                        var (m_M_\d+) = new MethodDefinition\("IFoo<Foo>.M",.+\);
                        \s+(cls_foo_\d+).Methods.Add\(\1\);
                        \s+m_M_5.Body.InitLocals = true;
                        \s+var il_M_\d+ = m_M_\d+.Body.GetILProcessor\(\);
                        \s+m_M_\d+.Overrides.Add\(new MethodReference\(m_M_2.Name, m_M_2.ReturnType\) {.+DeclaringType = itf_iFoo_\d+.MakeGenericInstanceType\(\2\),.+\);
                        """,
        TestName = "Generics")]
    [TestCase(
        "interface IFoo { int P { get; set; } } class Foo : IFoo { int IFoo.P { get => 42; set {} } }",
            """
                        (m_get_\d+).Overrides.Add\(m_get_\d+\);
                        \s+cls_foo_\d+.Methods.Add\(\1\);
                        """,
        TestName = "Property")]
    public void ExplicitInterfaceImplementation_SetsOverrideProperty(string source, string expectedRegex)
    {
        var result = RunCecilifier(source);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedRegex));
    }
}
