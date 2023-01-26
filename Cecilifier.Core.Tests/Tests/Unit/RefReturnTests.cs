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
                                @"\1Call, assembly.MainModule.ImportReference\(TypeHelpers.ResolveMethod\(typeof\(System.Span<System.Int32>\), ""get_Item"",System.Reflection.BindingFlags.Default\|System.Reflection.BindingFlags.Instance\|System.Reflection.BindingFlags.Public, ""System.Int32""\)\)\);\s+" +
                                @"(?:.+\s+){1,3}" + // In `simple` test there are one instruction (ldic4 42); in `complex` one there are 3 (ldarg1, ldci4 3, mul)
                                @"\1Stind_I4\);";
            
        Assert.That(cecilifiedCode, Does.Match(expectedIlSnippet), cecilifiedCode);
    }

    [TestCase("r", TestName = "Local")]
    [TestCase("s[3]", TestName = "Indexer")]
    [TestCase("Prop", TestName = "Property")]
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

    [TestCase("int i = s[4];", TestName = "Local Variable Initialization")]
    [TestCase("int i; i = s[4];", TestName = "Local Variable")]
    [TestCase("field = s[5];", TestName = "Field")]
    [TestCase("var localInferred = s[6];", TestName = "Inferred Local Variable")]
    [TestCase("nonRef = s[7];", TestName = "Parameter")]
    public void Assigning_RefReturn_EmitsLdind(string access)
    {
        var code = 
            $@"class ByRefReference 
            {{ 
                int field;
                void M(System.Span<int> s, ref int pi, int nonRef)
                {{
                    {access}         
                }}
            }}";
        
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring("OpCodes.Ldind_I4"));
    }
    
    [TestCase("int R(System.Span<int> s) { return s[8]; }", TestName = "Method")]
    [TestCase("int R(System.Span<int> s) => s[8];", TestName = "Bodied Method")]
    [TestCase("int P { get { return Get()[9]; } }", TestName = "Property")]
    [TestCase("int P => Get()[9];", TestName = "Bodied Property")]
    public void Returning_RefReturn_AsNonRef_EmitLdind(string memberDeclaration)
    {
        var code = 
            $@"
            using System;
            class ByRefReference 
            {{ 
                {memberDeclaration}
                Span<int> Get() => new int[10];
            }}";
        
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring("OpCodes.Ldind_I4"));
    }
    
    [TestCase("ref int R(System.Span<int> s) { return ref s[8]; }", TestName = "Method 1")]
    [TestCase("ref int R(System.Span<int> s) => ref s[8];", TestName = "Bodied Method 1")]
    [TestCase("ref int P { get { return ref Get()[9]; } }", TestName = "Property 1")]
    [TestCase("ref int P => ref Get()[9];", TestName = "Bodied Property 1")]
    public void Returning_RefReturn_AsRef_DoeNotEmitLdind(string memberDeclaration)
    {
        var code = 
            $@"
            using System;
            class ByRefReference 
            {{ 
                {memberDeclaration}
                Span<int> Get() => new int[10];
            }}";
        
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Not.Contains("Ldind"));
    }
}
