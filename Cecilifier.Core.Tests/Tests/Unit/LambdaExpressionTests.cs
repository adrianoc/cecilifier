using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class LambdaExpressionTests : CecilifierUnitTestBase
{
    [TestCase("using System; class Foo { void M(Func<int, int> a) { M(x => x + 1); } }", TestName = "Expression")]
    [TestCase("using System; class Foo { void M(Func<int, int> a) { M(x => { return x + 1; } ); } }", TestName = "Statement")]
    public void LambdaBodyIsProcessed(string source)
    {
        var result = RunCecilifier(source);
        Assert.That(
            result.GeneratedCode.ReadToEnd(),
            Does.Match(@"(il_lambda_.+\.Emit\(OpCodes\.)Ldarg_0\);\s+" +
                       @"\1Ldc_I4, 1\);\s+" +
                       @"\1Add\);\s+" +
                       @"\1Ret\);"));
    }

    [TestCase("using System; class Foo { void M() { Func<int, int> f = x => x + 1; Console.WriteLine(f(10)); } }", @"Ldc_I4, 10", TestName = "Simple")]
    [TestCase("using System; class Foo { void M(int p) { Func<int, int> f = x => x + 1; Console.WriteLine(f(p)); } }", "Ldarg_1", TestName = "UsingParameters")]
    public void ResultingDelegateInvocation(string source, string expectedLoadInstruction)
    {
        var result = RunCecilifier(source);
        Assert.That(
            result.GeneratedCode.ReadToEnd(),
            Does.Match(@"(il_M_\d+.Emit\(OpCodes\.)Ldnull\);\s+" +
                             @"\1Ldftn,.+m_lambda_.+\);\s+" +
                             @"\1Newobj, .+typeof\(System.Func<System.Int32, System.Int32>\).+\);\s+" +
                             @"\1Stloc, (l_f_\d+)\);\s+" +
                             @"//Console.WriteLine\(f\(.+\)\);\s+" +
                             @"\1Ldloc, \2\);\s+" +
                             @$"\1{expectedLoadInstruction}\);\s+" +
                             @"\1Callvirt, .+Invoke.+\);\s+" +
                             @"\1Call, .+WriteLine.+\);\s+"));
    }


    [Test]
    public void UsedInTopLevelExpressions()
    {
        var result = RunCecilifier("using System; Func<int, int> f = x => x + 1; Console.WriteLine(f(10));");
        Assert.That(
            result.GeneratedCode.ReadToEnd(),
            Does.Match(@"(il_topLevelMain_\d+.Emit\(OpCodes\.)Ldnull\);\s+" +
                             @"\1Ldftn,.+m_lambda_.+\);\s+" +
                             @"\1Newobj, .+typeof\(System.Func<System.Int32, System.Int32>\).+\);\s+" +
                             @"\1Stloc, (l_f_\d+)\);\s+" +
                             @"\1Ldloc, \2\);\s+" +
                             @"\1Ldc_I4, 10\);\s+" +
                             @"\1Callvirt, .+Invoke.+\);\s+" +
                             @"\1Call, .+WriteLine.+\);\s+"));
    }
}
