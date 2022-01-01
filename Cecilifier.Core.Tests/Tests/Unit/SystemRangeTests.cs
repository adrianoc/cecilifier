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
    
    private const string ResolvedSystemRangeCtor = @"assembly.MainModule.ImportReference\(TypeHelpers.ResolveMethod\(""System.Private.CoreLib"", ""System.Range"", "".ctor"",.+,"""", ""System.Index"", ""System.Index""\)\)";
}
