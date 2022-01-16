using System;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class SystemRangeTests : CecilifierUnitTestBase
{
    [Test]
    public void RangeCtor_IntFromEnd_IntFromEnd()
    {
        var result = RunCecilifier("using System; class Foo { void M() { Range r = ^8..^7; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_2\.Emit\(OpCodes\.)Ldc_I4, 8\);\s+" +
			@"\1Ldc_I4_1\);\s+" +
            @"\1Newobj, " + SystemIndexTests.ResolvedSystemIndexCtor + @"\);\s+" + 
            @"\1Ldc_I4, 7\);\s+" +
            @"\1Ldc_I4_1\);\s+" +
            @"\1Newobj, " + SystemIndexTests.ResolvedSystemIndexCtor + @"\);\s+" +
            @"\1Newobj, " + ResolvedSystemRangeCtor + @"\);\s+"));
    }
    
    [Test]
    public void RangeCtor_Int_IntFromEnd()
    {
        var result = RunCecilifier("using System; class Foo { void M() { Range r = 2..^2; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_2\.Emit\(OpCodes\.)Ldc_I4, 2\);\s+" +
            @$"\1Call, {SystemIndexTests.ResolvedSystemIndexOpImplicit}\);\s+" +
            @"\1Ldc_I4, 2\);\s+" +
            @"\1Ldc_I4_1\);\s+" +
            @$"\1Newobj, {SystemIndexTests.ResolvedSystemIndexCtor}\);\s+" +
            @$"\1Newobj, {ResolvedSystemRangeCtor}\);\s+"));
    }
    
    [Test]
    public void RangeCtor_Int_Int()
    {
        var result = RunCecilifier("using System; class Foo { void M() { Range r = 3..4; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_2\.Emit\(OpCodes\.)Ldc_I4, 3\);\s+" +
            @$"\1Call, {SystemIndexTests.ResolvedSystemIndexOpImplicit}\);\s+" +
            @"\1Ldc_I4, 4\);\s+" +
            @$"\1Call, {SystemIndexTests.ResolvedSystemIndexOpImplicit}\);\s+" +
            @$"\1Newobj, {ResolvedSystemRangeCtor}\);\s+"));
    }
    
    [Test]
    public void RangeCtor_Int_Index()
    {
        var result = RunCecilifier("using System; class Foo { void M(Index i) { Range r = 5..i; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_2\.Emit\(OpCodes\.)Ldc_I4, 5\);\s+" +
            @$"\1Call, {SystemIndexTests.ResolvedSystemIndexOpImplicit}\);\s+" +
            @"\1Ldarg_1\);\s+" +
            @$"\1Newobj, {ResolvedSystemRangeCtor}\);\s+"));
    }
    
    [Test]
    public void RangeCtor_Index_Int()
    {
        var result = RunCecilifier("using System; class Foo { void M(Index i) { Range r = i..6; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_2\.Emit\(OpCodes\.)Ldarg_1\);\s+" +
            @"\1Ldc_I4, 6\);\s+" +
            @$"\1Call, {SystemIndexTests.ResolvedSystemIndexOpImplicit}\);\s+" +
            @$"\1Newobj, {ResolvedSystemRangeCtor}\);\s+"));
    }
    
    [Test]
    public void RangeCtor_Index_Index()
    {
        var result = RunCecilifier("using System; class Foo { void M(Index i, Index j) { Range r = i..j; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_2\.Emit\(OpCodes\.)Ldarg_1\);\s+" +
            @"\1Ldarg_2\);\s+" +
            @$"\1Newobj, {ResolvedSystemRangeCtor}\);\s+"));
    }
    
    [TestCase("2", "^3")]
    [TestCase("i", "^3")]
    [TestCase("Idx()", "^Idx()")]
    [TestCase("i", "^Idx()")]
    [TestCase("2", "^i")]
    public void SpanIndexer_WithRangeExpression_FromBeginEnd_IsMappedToSlice(string startIndex, string endIndex)
    {
        var result = RunCecilifier($"using System; class Foo {{ static int Idx() => 42; Span<int> M(int i, Span<int> s) => s[{startIndex}..{endIndex}]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var indexedFromEnd = endIndex[0] == '^';
        var expected =
            @".+(il_M_\d+\.Emit\(OpCodes\.)Ldarg_2\);\s+" +
            @"var (?<span_copy>.+SpanCopy_\d) = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<span_copy>\);\s+" +
            @"\1Stloc, \k<span_copy>\);\s+" +
            @"\1Ldloca, \k<span_copy>\);\s+".IfTrue(indexedFromEnd) +
            @"\1(?:Ldc_I4, 2|Ldarg_1|Call, m_idx_\d)\);\s+" +
            @"var (?<start_index>l_startIndex_\d) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<start_index>\);\s+" +
            @"\1Stloc, \k<start_index>\);\s+" +
            @"\1Dup\);\s+" +
            @"\1Call, assembly.MainModule.ImportReference\(.+ResolveMethod\(.+, ""System.Span`1"", ""get_Length"",.+,""System.Int32""\)\)\);\s+".IfTrue(indexedFromEnd) +
            @"\1Conv_I4\);\s+" + 
            @"\1(Ldc_I4, \d+|Ldarg_1|Call, m_idx_\d)\);\s+"+
            @"\1Sub\);\s+".IfTrue(indexedFromEnd) +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Sub\);\s+" + 
            @"var (?<ec>l_elementCount_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<ec>\);\s+"+ 
            @"\1Stloc, \k<ec>\);\s+" +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Ldloc, \k<ec>\);\s+" +
            @"\1Call,.+""System.Span`1"", ""Slice"",.+,""System.Int32"", ""System.Int32"", ""System.Int32"".+;";

        Assert.That(cecilifiedCode, Does.Match(expected));
    }
    
    [TestCase("i", "3")]
    [TestCase("2", "i")]
    [TestCase("2", "4")]
    [TestCase("Idx()", "Idx()")]
    [TestCase("Idx()", "i")]
    [TestCase("i", "Idx()")]
    public void SpanIndexer_WithRangeExpression_FromBeginBegin_IsMappedToSlice(string startIndex, string endIndex)
    {
        var result = RunCecilifier($"using System; class Foo {{ static int Idx() => 42; Span<int> M(int i, Span<int> s) => s[{startIndex}..{endIndex}]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var indexedFromEnd = endIndex[0] == '^';
        var expected =
            @".+(il_M_\d+\.Emit\(OpCodes\.)Ldarg_2\);\s+" +
            @"var (?<span_copy>.+SpanCopy_\d) = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<span_copy>\);\s+" +
            @"\1Stloc, \k<span_copy>\);\s+" +
            @"\1Ldloca, \k<span_copy>\);\s+" +
            @"\1(?:Ldc_I4, 2|Ldarg_1|Call, m_idx_\d)\);\s+" +
            @"var (?<start_index>l_startIndex_\d) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<start_index>\);\s+" +
            @"\1Stloc, \k<start_index>\);\s+" +
            @"\1(Ldc_I4, \d+|Ldarg_1|Call, m_idx_\d)\);\s+"+
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Sub\);\s+"+
            @"var (?<ec>l_elementCount_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<ec>\);\s+"+ 
            @"\1Stloc, \k<ec>\);\s+" +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Ldloc, \k<ec>\);\s+" +
            @"\1Call,.+""System.Span`1"", ""Slice"",.+,""System.Int32"", ""System.Int32"", ""System.Int32"".+;";

        Assert.That(cecilifiedCode, Does.Match(expected));
    }
    
    [TestCase("^2", "3")]
    [TestCase("^Idx()", "Idx()")]
    [TestCase("^i", "3")]
    public void SpanIndexer_WithRangeExpression_FromEndBegin_IsMappedToSlice(string startIndex, string endIndex)
    {
        var result = RunCecilifier($"using System; class Foo {{ static int Idx() => 42; Span<int> M(int i, Span<int> s) => s[{startIndex}..{endIndex}]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var expected =
            @".+(il_M_\d+\.Emit\(OpCodes\.)Ldarg_2\);\s+" +
            @"var (?<span_copy>.+SpanCopy_\d) = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<span_copy>\);\s+" +
            @"\1Stloc, \k<span_copy>\);\s+" +
            @"\1Ldloca, \k<span_copy>\);\s+" +
            @"\1Dup\);\s+" +
            @"\1Call, assembly.MainModule.ImportReference\(.+ResolveMethod\(.+, ""System.Span`1"", ""get_Length"",.+,""System.Int32""\)\)\);\s+" +
            @"\1Conv_I4\);\s+" + 
            @"\1(Ldc_I4, \d+|Ldarg_1|Call, m_idx_\d)\);\s+"+
            @"\1Sub\);\s+" +
            @"var (?<start_index>l_startIndex_\d) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<start_index>\);\s+" +
            @"\1Stloc, \k<start_index>\);\s+" +
            @"\1(Ldc_I4, \d+|Ldarg_1|Call, m_idx_\d)\);\s+"+
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Sub\);\s+" + 
            @"var (?<ec>l_elementCount_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<ec>\);\s+"+ 
            @"\1Stloc, \k<ec>\);\s+" +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Ldloc, \k<ec>\);\s+" +
            @"\1Call,.+""System.Span`1"", ""Slice"",.+,""System.Int32"", ""System.Int32"", ""System.Int32"".+;";

        Assert.That(cecilifiedCode, Does.Match(expected));
    }
    
    [TestCase("^4", "^2")]
    [TestCase("^Idx()", "^Idx()")]
    public void SpanIndexer_WithRangeExpression_FromEndEnd_IsMappedToSlice2(string startIndex, string endIndex)
    {
        var result = RunCecilifier($"using System; class Foo {{ static int Idx() => 42; Span<int> M(int i, Span<int> s) => s[{startIndex}..{endIndex}]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var indexedFromEnd = endIndex[0] == '^';
        var expected =
            @".+(il_M_\d+\.Emit\(OpCodes\.)Ldarg_2\);\s+" +
            @"var (?<span_copy>.+SpanCopy_\d) = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<span_copy>\);\s+" +
            @"\1Stloc, \k<span_copy>\);\s+" +
            @"\1Ldloca, \k<span_copy>\);\s+".IfTrue(indexedFromEnd) +
            @"\1Dup.+\s+" +
            @"\1Call, assembly.MainModule.ImportReference\(.+ResolveMethod\(.+, ""System.Span`1"", ""get_Length"",.+,""System.Int32""\)\)\);\s+" +
            @"\1Conv_I4\);\s+" + 
            @"\1(Ldc_I4, \d+|Call, m_idx_\d)\);\s+"+
            @"\1Sub\);\s+" +
            @"var (?<start_index>l_startIndex_\d) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<start_index>\);\s+" +
            @"\1Stloc, \k<start_index>\);\s+" +
            @"\1Dup.+\s+" +
            @"\1Call, assembly.MainModule.ImportReference\(.+ResolveMethod\(.+, ""System.Span`1"", ""get_Length"",.+,""System.Int32""\)\)\);\s+" +
            @"\1Conv_I4\);\s+" + 
            @"\1(Ldc_I4, \d+|Call, m_idx_\d)\);\s+"+
            @"\1Sub\);\s+" +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Sub\);\s+" + 
            @"var (?<ec>l_elementCount_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<ec>\);\s+"+ 
            @"\1Stloc, \k<ec>\);\s+" +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Ldloc, \k<ec>\);\s+" +
            @"\1Call,.+""System.Span`1"", ""Slice"",.+,""System.Int32"", ""System.Int32"", ""System.Int32"".+;";

        Assert.That(cecilifiedCode, Does.Match(expected));
    }

    [TestCase("r")]
    [TestCase("o.rf")]
    [TestCase("o.M()")]
    [TestCase("M()")]
    public void SpanIndexer_WithStoredRange_IsMappedToSlice(string range)
    {
        var result = RunCecilifier($"using System; class Foo {{ Range rf; Range M() => rf; Span<int> M(Span<int> s, Foo o, Range r) => s[{range}]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var expected =
            @".+(il_M_\d+\.Emit\(OpCodes\.)Ldarg_1\);\s+" +
            @"var (?<span_copy>.+SpanCopy_\d) = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<span_copy>\);\s+" +
            @"\1Stloc, \k<span_copy>\);\s+" +
            @"\1Ldloca, \k<span_copy>\);\s+" +
            @"\1Call, assembly.MainModule.ImportReference\(.+ResolveMethod\(.+, ""System.Span`1"", ""get_Length"",.+,""System.Int32""\)\)\);\s+" +
            @"var (?<span_length>.+spanLength.+\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<span_length>\);\s+" +
            @"\1Stloc, \k<span_length>\);\s+" +
            
            @"(?:\1Ldarg_3\);\s+" + @"|\1Ldarg_2\);\s+\1Ldfld,.+\);\s+" + @"|\1Ldarg_0\);\s+\1Call,.+\);\s+" + @"|\1Ldarg_2\);\s+\1Callvirt,.+\);\s+)" + 
            
            @"var (?<range_var>.+rangeVar_\d+) = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(Range\)\)\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<range_var>\);\s+" +
            @"\1Stloc, \k<range_var>\);\s+" +
            @"\1Ldloca, \k<range_var>\);\s+" +
            @"\1Call, assembly.MainModule.ImportReference\(.+ResolveMethod\(.+, ""System.Range"", ""get_Start"",.+\)\)\);\s+" +
            @"var (?<index_var>.+index_\d+) = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(Index\)\)\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<index_var>\);\s+" +
            @"\1Stloc, \k<index_var>\);\s+" +
            @"\1Ldloca, \k<index_var>\);\s+" +
            @"\1Ldloc, \k<span_length>\);\s+" +
            @"\1Call,.+""System.Index"", ""GetOffset"",.+, ""System.Int32"".+;\s+" +
            @"var (?<start_index>.+startIndex.+\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"(?<body_var>.+)\.Variables.Add\(\k<start_index>\);\s+" +
            @"\1Stloc, \k<start_index>\);\s+" +
            @"\1Ldloca, \k<range_var>\);\s+" +
            @"\1Call, assembly.MainModule.ImportReference\(.+ResolveMethod\(.+, ""System.Range"", ""get_End"",.+\)\)\);\s+" +
            @"\1Stloc, \k<index_var>\);\s+" +
            @"\1Ldloca, \k<index_var>\);\s+" +
            @"\1Ldloc, \k<span_length>\);\s+" +
            @"\1Call,.+""System.Index"", ""GetOffset"",.+, ""System.Int32"".+;\s+" +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Sub\);\s+" +
            @"var (?<ec>l_elementCount_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
            @"\k<body_var>.Variables.Add\(\k<ec>\);\s+"+ 
            @"\1Stloc, \k<ec>\);\s+" +
            @"\1Ldloca, \k<span_copy>\);\s+" +
            @"\1Ldloc, \k<start_index>\);\s+" +
            @"\1Ldloc, \k<ec>\);\s+" +
            @"\1Call,.+""System.Span`1"", ""Slice"",.+,""System.Int32"", ""System.Int32"", ""System.Int32"".+;" +
            "";
       
        Assert.That(cecilifiedCode, Does.Match(expected));
    }

    private const string ResolvedSystemRangeCtor = @"assembly.MainModule.ImportReference\(TypeHelpers.ResolveMethod\(""System.Private.CoreLib"", ""System.Range"", "".ctor"",.+,"""", ""System.Index"", ""System.Index""\)\)";
}

static class Conditional
{
    public static string IfTrue(this string self, bool expression) => expression ? self : string.Empty;
}
