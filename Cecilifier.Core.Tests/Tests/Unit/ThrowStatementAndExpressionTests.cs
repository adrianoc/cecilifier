using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ThrowStatementAndExpressionTests : CecilifierUnitTestBase
{
    [Test]
    public void TestThrowStatement()
    {
        var result = RunCecilifier("class Foo { void ThrowStatement() { throw new System.NotSupportedException(\"ex\"); } }");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match(
                @"il_throwStatement_2.Emit\(OpCodes.Ldstr,.+""ex""\);\s+" +
                @"il_throwStatement_2.Emit\(OpCodes.Newobj,.+""System.NotSupportedException"", "".ctor"",.+\);\s+" + 
                @"il_throwStatement_2.Emit\(OpCodes.Throw\);\s+"));
    }

    [Test]
    public void TestThrowExpression()
    {
        var result = RunCecilifier("class Foo { int ThrowExpression(int i) => i > 10 ? i + 1 : throw new System.Exception(\"ex\"); }");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match(
                @"var lbl_conditionEnd_\d = il_throwExpression_2.Create\(OpCodes.Nop\);\s+"+
			    @"var lbl_whenFalse_\d = il_throwExpression_2.Create\(OpCodes.Nop\);\s+" + 
                @"(il_throwExpression_\d+\.Emit\(OpCodes\.)Ldarg_1\);\s+" + 
                @"\1Ldc_I4, 10\);\s+" + 
                @"\1Cgt\);\s+" + 
                @"\1Brfalse_S, lbl_whenFalse_\d+\);\s+" + 
                @"\1Ldarg_1\);\s+" + 
                @"\1Ldc_I4, 1\);\s+" + 
                @"\1Add\);\s+" + 
                @"\1Br_S, lbl_conditionEnd_\d\);\s+" + 
                @"il_throwExpression_2.Append\(lbl_whenFalse_\d+\);\s+" + 
                @"il_throwExpression_2.Emit\(OpCodes.Ldstr,.+""ex""\);\s+" +
                @"\1Newobj,.+""System.Exception"", "".ctor"",.+;\s+" + 
                @"\1Throw\);\s+" + 
                @"il_throwExpression_2.Append\(lbl_conditionEnd_\d+\);"));
    }
}
