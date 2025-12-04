using System.Linq;
using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class DelegateTests : CecilifierUnitTestBase
{
    [Test]
    public void InnerDelegateDeclaration()
    {
        var result = RunCecilifier("class C { public delegate int D(); }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var matches = Regex.Matches(cecilifiedCode, @"var (del_D\d+) = new TypeDefinition\("""", ""D"", .+ImportReference\(typeof\(System.MulticastDelegate\)\)\);");
        Assert.That(matches.Count, Is.EqualTo(1));

        Assert.That(cecilifiedCode, Does.Match(@"var m_invoke_\d+ = new MethodDefinition\(""Invoke"", .+assembly.MainModule.TypeSystem.Int32\)"));

        Assert.That(cecilifiedCode, Does.Match(@"var m_beginInvoke_\d+ = new MethodDefinition\(""BeginInvoke"",.+ImportReference\(typeof\(System\.IAsyncResult\)\)\).*"));
        Assert.That(cecilifiedCode, Does.Match(@"m_beginInvoke_\d+.Parameters.Add\(new ParameterDefinition\(assembly.MainModule.ImportReference\(typeof\(System.AsyncCallback\)\)\)\);\s+"));
        Assert.That(cecilifiedCode, Does.Match(@"m_beginInvoke_\d+.Parameters.Add\(new ParameterDefinition\(assembly.MainModule.TypeSystem.Object\)\);\s+"));

        Assert.That(cecilifiedCode, Does.Match(@"var m_endInvoke_\d+ = new MethodDefinition\(""EndInvoke"",.+assembly.MainModule.TypeSystem.Int32\);\s+"));
        Assert.That(cecilifiedCode, Does.Match(@"var p_ar_\d+ = new ParameterDefinition\(""ar"",.+ImportReference\(typeof\(System.IAsyncResult\)\)\);"));

        Assert.That(cecilifiedCode, Does.Match(@"cls_C_\d+.NestedTypes.Add\(del_D\d+\);"), "Inner delegate should be added as a nested type of C");
    }

    [Test]
    public void InnerDelegate_AsParameter()
    {
        var result = RunCecilifier("class C { public delegate int D(); D M(D p) => p; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(@"var m_M_\d+ = new MethodDefinition\(""M"",.+, del_D\d+\);"), "Return type");
        Assert.That(cecilifiedCode, Does.Match(@"var p_p_\d+ = new ParameterDefinition\(""p"",.+del_D\d+\);"), "Parameter");
    }

    [Test]
    public void FirstStaticMethodToDelegateConversion_IsCached()
    {
        var result = RunCecilifier("class C { System.Action<int> Instantiate() { return M; } static void M(int i) { } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(@"var (fld_cachedDelegate_\d+) = new FieldDefinition\(""<0>__M"", FieldAttributes.Public \| FieldAttributes.Static, assembly.MainModule.ImportReference\(typeof\(System.Action<>\)\).+;"));

        Assert.That(cecilifiedCode, Does.Match(@"il_instantiate_\d+.Emit\(OpCodes.Ldsfld, fld_cachedDelegate_\d+\);"));
        Assert.That(cecilifiedCode, Does.Match(@"il_instantiate_\d+.Emit\(OpCodes.Dup\);"));
        Assert.That(cecilifiedCode, Does.Match(@"il_instantiate_\d+.Emit\(OpCodes.Brtrue, lbl_cacheHit_\d+\);"));  // Check if already instantiated...
        Assert.That(cecilifiedCode, Does.Match(@"il_instantiate_\d+.Emit\(OpCodes.Stsfld, fld_cachedDelegate_\d+\);")); // Set the cache.
    }

    [Test]
    public void SecondStaticMethodToDelegateConversion_OfSameMethod_IsCached()
    {
        var result = RunCecilifier("class C { System.Action<int> I1() { return M; } System.Action<int> I2() { return M; } static void M(int i) { } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var matches = Regex.Matches(cecilifiedCode, @".+FieldDefinition\(""<\d+>__M\d?"".+\);");
        Assert.That(matches.Count, Is.EqualTo(1), matches.Aggregate("Only one backing field was expected.\n", (acc, curr) => $"{acc}\n{curr.Value}") + $"\n\nCode:\n{cecilifiedCode}");
    }

    [Test]
    public void SecondStaticMethodToDelegateConversion_OfDifferentMethod_IsCached()
    {
        var result = RunCecilifier("class C { void I2() { System.Action<int> d1 = M; d1 = M2; } static void M(int i) { } static void M2(int i) { } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var matches = Regex.Matches(cecilifiedCode, @".+FieldDefinition\(""<\d+>__M\d?"".+\);");
        Assert.That(matches.Count, Is.EqualTo(2), matches.Aggregate("Expecting 2 backing field.\n", (acc, curr) => $"{acc}\n{curr.Value}") + $"\n\nCode:\n{cecilifiedCode}");
    }

    [Test]
    public void StaticMethodToDelegateConversion_FromOtherMethod_GeneratesWarning()
    {
        var result = RunCecilifier("class Foo { public static void M(int i) { } } class Bar { System.Action<int> Instantiate() { return Foo.M; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring("#warning Converting static method (M) to delegate in a type other than the one defining it may generate incorrect code. Access type: Bar, Method type: Foo"));
    }

    [TestCase(@"class C { float F() => 42.0F; void M() { var r = new System.Func<float>(F); } }", TestName = "Explicit")]
    [TestCase(@"class C { float F() => 42.0F; void M(System.Func<float> r) { M(F); } }", TestName = "Through method group conversion")]
    public void DelegateInstantiation(string source)
    {
        var result = RunCecilifier(source);
        Assert.That(
            result.GeneratedCode.ReadToEnd(),
            Does.Match(@"(il_M_4\.Emit\(OpCodes\.)Ldarg_0\);\s+" +
                       @"\1Ldftn, m_F_1\);\s+" +
                       @"\1Newobj, .+typeof\(System.Func<System\.Single>\).+System\.Object.+System\.IntPtr.+\);\s+"));
    }
}
