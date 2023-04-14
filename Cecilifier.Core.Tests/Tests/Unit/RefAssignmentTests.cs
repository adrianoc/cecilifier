using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class RefAssignmentTests : CecilifierUnitTestBase
{
    [TestCase(
        "rl = 42",
        """
        (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloc, l_rl_\d+\);
        \s+\1Ldc_I4, 42\);
        \s+\1Stind_I4\);
        """,
        TestName = "SimpleAssignmentLocal")]

    [TestCase(
        "rl = ref i",
        // we expect the instruction sequence Ldloca, l_i_n / Stloc, l_rl_n twice: one for `rl` variable initialization
        // (common to all ref local assignment tests) and the second one for the variable assignment itself.
        """
        (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca, l_i_\d+\);
        \s+\1Stloc, l_rl_\d+\);
        \s+\1Ldloca, l_i_\d+\);
        \s+\1Stloc, l_rl_\d+\);
        """,
        TestName = "RefAssignmentLocal")]
    public void TestRefLocalAssignment(string assignmentExpression, string expected)
    {
        var result = RunCecilifier($"int i = 10; ref int rl = ref i; {assignmentExpression};");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(expected));
    }

    [TestCase(
        "rp = 10",
        """
        (il_bar_\d+\.Emit\(OpCodes\.)Ldarg_1\);
        \s+\1Ldc_I4, 10\);
        \s+\1Stind_I4\);
        """,
        TestName = "SimpleAssignment")]

    [TestCase(
        "rp = ref intField",
        """
        (il_bar_\d+\.Emit\(OpCodes\.)Ldarg_0\);
        \s+\1Ldflda, fld_intField_\d+\);
        \s+\1Starg_S, p_rp_\d+\);
        """,
        TestName = "RefAssignment")]
    public void TestRefParameterAssignment(string assignmentExpression, string expected)
    {
        var result = RunCecilifier($"class Foo {{ int intField; void Bar(ref int rp) => {assignmentExpression}; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(expected));
    }

    [Test]
    public void TesRefParameterDeference()
    {
        var result = RunCecilifier("void M(ref int r) { int i; i = r + 42; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(
                """
            (il_M_\d+.Emit\(OpCodes\.)Ldarg_1\);
            \s+\1Ldind_I4\);
            \s+\1Ldc_I4, 42\);
            \s+\1Add\);
            \s+\1Stloc, l_i_9\);
            """));
    }

    [Test]
    public void TesRefFieldAssignment()
    {
        var result = RunCecilifier("ref struct RefStruct { ref int refInt; public RefStruct(ref int r) { refInt = ref r; refInt = 42; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode,
            Does.Match(
                """
            //refInt = ref r;
            \s+var (ldarg_\d+_\d+) = (il_ctor_\d+).Create\(OpCodes.Ldarg_0\);
            \s+\2.Append\(\1\);
            \s+\2.Emit\(OpCodes.Ldarg_1\);
            \s+\2.Emit\(OpCodes.Stfld, fld_refInt_\d+\);
            """),
            "Ref Assignment");

        Assert.That(
            cecilifiedCode,
            Does.Match(
                """
            //refInt = 42;
            \s+(il_ctor_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \s+\1Ldfld, fld_refInt_\d+\);
            \s+\1Ldc_I4, 42\);
            \s+\1Stind_I4\);
            """),
            "Simple Assignment");
    }
}
