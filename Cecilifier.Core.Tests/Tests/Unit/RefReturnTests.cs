using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class RefReturnTests : CecilifierUnitTestBase
{
    [TestCase("42", TestName = "Simple")]
    [TestCase("n * 3", TestName = "Complex")]
    public void TestSpanIndexAssignment(string valueToAssign)
    {
        var result = RunCecilifier($"using System; class StackAlloc {{ private void M(int n, Span<int> s) {{ s[50] = {valueToAssign};	}} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var expectedIlSnippet = @"//s\[50\] = (?:.+);\s+" +	
                                @"(.+\.Emit\(OpCodes\.)Ldarga, p_s_4\);\s+" +
                                @"\1Ldc_I4, 50\);\s+" +
                                @"\1Call, assembly.MainModule.ImportReference\(TypeHelpers.ResolveMethod\(""System.Private.CoreLib"", ""System.Span`1"", ""get_Item"",System.Reflection.BindingFlags.Default\|System.Reflection.BindingFlags.Instance\|System.Reflection.BindingFlags.Public,""System.Int32"", ""System.Int32""\)\)\);\s+" +
                                @"(?:.+\s+){1,3}" + // In `simple` test there are one instruction (ldic4 42); in `complex` one there are 3 (ldarg1, ldci4 3, mul)
                                @"\1Stind_I4\);";
            
        Assert.That(cecilifiedCode, Does.Match(expectedIlSnippet), cecilifiedCode);
    }

    [TestCase("r", TestName = "Local")]
    [TestCase("s[3]", TestName = "Indexer"), Category("Issues,#141")]
    [TestCase("Prop", TestName = "Property"), Category("Issues,#141")]
    public void Passing_Ref_AsArgument(string refToUse)
    {
        var code = $@"using System;
class SpanIndexer
{{
    int _value;
    ref int Prop => ref _value;

	void M(Span<int> s, int i)
	{{
        ref int r = ref s[4];
        M(s, {refToUse});
	}}   
}}";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Contains.Substring("OpCodes.Ldind_I4"));
    }
}
