using System;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class SystemIndexTests : CecilifierUnitTestBase
{
    [TestCase("using System; class C { void M(Index index) { index = 1; } }", "Starg_S", TestName = "Parameter_IntegerAssignment")]
    [TestCase("using System; class C { Index index; void M() { index = 1; } }", "Stfld",TestName = "Field_IntegerAssignment")]
    [TestCase("using System; class C { void M() { Index index; index = 1; } }", "Stloc",TestName = "Local_IntegerAssignment")]
    [TestCase("using System; class C { void M() { Index index = 1; } }", "Stloc",TestName = "Local_IntegerInitialization")]
    public void ConversionOperator_IsCalled_OnAssignments(string code, string expectedStoreOpCode)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match($@"il_M_\d+.Emit\(OpCodes\.Call, {ResolvedSystemIndexOpImplicit}\);\s+"));
        Assert.That(cecilifiedCode, Does.Match($@"il_M_\d+.Emit\(OpCodes\.{expectedStoreOpCode}\s?,.+\);"));
    }

    [Test]
    public void IndexExpression_UsedToIndexArray()
    {
        var result = RunCecilifier(@"class C { int M(int []a) => a[^5]; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_\d\.Emit\(OpCodes\.)Ldarg_1\);\s+" +
            @"\1Dup.+\s+" +
			@"\1Ldlen.+\s+" +
			@"\1Conv_I4\);\s+" +
            @"\1Ldc_I4, 5\);\s+" +
            @"\1Sub.+\s+" +
			@"\1Ldelem_I4\);\s+" +
            @"\1Ret.+"));
    }
    
    [Test]
    public void IndexExpression_UsedToIndexSpan()
    {
        var result = RunCecilifier(@"class C { int M(System.Span<int> a) => a[^5]; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_\d\.Emit\(OpCodes\.)Ldarga,.+\);\s+" +
            @"\1Dup.+\s+" +
            @"\1Call, .+""System.Span`1"", ""get_Length"".+\);\s+" +
			@"\1Conv_I4\);\s+" +
            @"\1Ldc_I4, 5\);\s+" +
            @"\1Sub.+\s+" +
            @"\1Call,.+""System.Span`1"", ""get_Item"",.+\);\s+" +
            @"\1Ldind_I4\);\s+" +
            @"\1Ret.+"));
    }
    
    [TestCase("using System; class C { int M(int []a, Index index) => a[index]; }", TestName = "Parameter")]
    [TestCase("using System; class C { static Index index; int M(int []a) => a[index]; }", TestName = "Field")]
    [TestCase("using System; class C { int M(int []a) { Index index; index = 1; return a[index]; } }", TestName = "Local")]
    public void IndexVariable_UsedToIndexArray_GetOffset_IsCalled(string code)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"il_M_\d.Emit\(OpCodes.Ldarg_1\);\s+" +
            @"il_M_\d.Emit\(OpCodes.(Ldloca|Ldarga|Ldflda|Ldsflda), (p|l|fld)_index_\d\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ldarg_1\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ldlen\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Conv_I4\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Call, .+GetOffset.+\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ldelem_I4\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ret\);"));
    }

    [TestCase("Index field = ^1;", SymbolKind.Field, TestName = "Field Initialization")]
    [TestCase("Index field; void SetField() => field = ^11;", SymbolKind.Field, TestName = "Field Assignment")]
    [TestCase("void M() { Index local = ^2; }", SymbolKind.Local, TestName = "Local Variable Initialization")]
    [TestCase("void M() { Index local; local = ^2; }",  SymbolKind.Local, TestName = "Local Variable Assignment")]
    [TestCase("void M() { var local = ^3; }",  SymbolKind.Local, TestName = "Inferred Local Variable Initialization")]
    [TestCase("void Parameter(Index index) => index = ^11;",  SymbolKind.Parameter, TestName = "Parameter 2")]
    public void IndexFromEnd_ExplicitCtorIsInvoked(string indexSnippet, SymbolKind kind)
    {
        // Note that the compiler only call the ctor (instead of newobj) if the `location` being assigned is not being used
        // so cecilifier is not generating exactly the same code but but it is still valid.
        var result = RunCecilifier($"using System; class Foo {{ {indexSnippet} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Contains.Substring (kind.LoadAddressOpCode().ToString().PascalCase()), cecilifiedCode);
        Assert.That(cecilifiedCode, Does.Not.Contains(kind.StoreOpCode().ToString().PascalCase()), cecilifiedCode);
        Assert.That(cecilifiedCode, Does.Match(@".+OpCodes.Call, .+TypeHelpers.ResolveMethod\(""System.Private.CoreLib"", ""System.Index"", "".ctor"",.+,"""", ""System.Int32"", ""System.Boolean""\)\)\);"), cecilifiedCode);
    }

    [Test]
    public void IndexFromEnd_OnMemberReference_ExplicitCtorIsInvoked_WithNewObject()
    {
        var result = RunCecilifier("using System; class Foo { Index field; void SetField(Foo other) => other.field = ^11; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
     
        var expectedSnippet =
            @"(il_setField_\d\.Emit\(OpCodes\.)Ldarg_1\);\s+" +
            @"\1Ldc_I4, 11.+\s+" +
            @"\1Ldc_I4_1.+\s+" +
            @".+OpCodes.Newobj, .+TypeHelpers.ResolveMethod\(""System.Private.CoreLib"", ""System.Index"", "".ctor"",.+,"""", ""System.Int32"", ""System.Boolean""\)\)\);\s+" +
            @"\1Stfld, fld_field_\d";
        
        Assert.That(cecilifiedCode, Does.Match(expectedSnippet), cecilifiedCode);
    }

    [TestCase("Index Index => ^1;",  SymbolKind.Local, TestName = "Property")]
    [TestCase("Index Index2 { get => ^1; }",  SymbolKind.Local, TestName = "Property 2")]
    [TestCase("Index M3() { return ^3; }",  SymbolKind.Local, TestName = "Method")]
    [TestCase("Index M4() => ^4;",  SymbolKind.Local, TestName = "Method Bodied")]
    public void AsReturnValue(string indexSnippet, SymbolKind kind)
    {
        var result = RunCecilifier($"using System; class Foo {{ {indexSnippet} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Not.Contains(kind.LoadAddressOpCode().ToString().PascalCase()), cecilifiedCode);
        Assert.That(cecilifiedCode, Does.Match(@".+OpCodes.Newobj, .+TypeHelpers.ResolveMethod\(""System.Private.CoreLib"", ""System.Index"", "".ctor"",.+,"""", ""System.Int32"", ""System.Boolean""\)\)\);"), cecilifiedCode);

        Console.WriteLine(cecilifiedCode);
    }

    private const string ResolvedSystemIndexOpImplicit = @"assembly\.MainModule\.ImportReference\(TypeHelpers\.ResolveMethod\(""System\.Private\.CoreLib"", ""System\.Index"", ""op_Implicit"",System\.Reflection\.BindingFlags\.Default\|System\.Reflection.BindingFlags.Static\|System.Reflection.BindingFlags.Public,"""", ""System.Int32""\)\)";
}

internal static class SymbolKindExtensions
{
    public static OpCode LoadAddressOpCode(this SymbolKind self) => self switch
    {
        SymbolKind.Local => OpCodes.Ldloca,
        SymbolKind.Parameter => OpCodes.Ldarga,
        SymbolKind.Field => OpCodes.Ldflda,
        _ => throw new NotSupportedException($"I don't think we can get the address of '{self}'")
    };
    
    public static OpCode StoreOpCode(this SymbolKind self) => self switch
    {
        SymbolKind.Local => OpCodes.Stloc,
        SymbolKind.Parameter => OpCodes.Starg,
        SymbolKind.Field => OpCodes.Stfld,
        _ => throw new NotSupportedException($"I don't think we store data in '{self}'")
    };
}

