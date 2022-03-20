using System.Text.RegularExpressions;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;


/// <summary>
/// These tests covers scenarios in which we end up with a call on a value type.
/// </summary>
[TestFixture]
public class InvocationOnValueTypeTests : CecilifierUnitTestBase
{
    [TestCase("using System; class C { int M(int []a, Index index) { Index l = index; return a[l]; } }", "Ldloca", TestName = "Local_UsedAsIndex")]
    [TestCase("using System; class C { int M(int []a, Index index) => a[index]; }", "Ldarga", TestName = "Parameter_UsedAsIndex")]
    [TestCase("using System; class C { Index index; int M(int []a) => a[index]; }", "Ldflda", TestName = "Field_UsedAsIndex")]
    [TestCase("struct S { public int Prop {get; set; } } class C { int M(S s) => s.Prop; }", "Ldarga", TestName = "Property access on parameter")]
    [TestCase("struct S { public int Prop {get; set; } } class C { int M(S s) { S l = s; return l.Prop; } }", "Ldloca", TestName = "Property access on local")]
    [TestCase("struct S { public int Prop {get; set; } } class C { S f; int M()  => f.Prop; }", "Ldflda", TestName = "Property access on field")]
    [TestCase("using System; struct S { public event Action E; } class C { void M(){} void M(S s) { s.E +=M; } }", "Ldarga", TestName = "Event access on parameter")]
    public void Test_SystemIndex(string snippet, string expectedOpCode)
    {
        var result = RunCecilifier(snippet);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var found = Regex.Matches(cecilifiedCode, expectedOpCode);
        Assert.That(found.Count, Is.EqualTo(1), $"Mismatch in expected number of {expectedOpCode}\n\n{cecilifiedCode}");
    }
}
