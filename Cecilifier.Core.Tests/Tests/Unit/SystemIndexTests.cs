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

    [TestCase("using System; class C { int M(int []a, Index index) => a[index]; }", TestName = "Parameter")]
    [TestCase("using System; class C { static Index index; int M(int []a) => a[index]; }", TestName = "Field")]
    [TestCase("using System; class C { int M(int []a) { Index index; index = 1; return a[index]; } }", TestName = "Local")]
    public void UsedToIndexArray_GetOffset_IsCalled(string code)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"il_M_\d.Emit\(OpCodes.Ldarg_1\);\s+" +
            @"il_M_\d.Emit\(OpCodes.(Ldloca|Ldarga|Ldflda), (p|l|fld)_index_\d\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ldarg_1\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ldlen\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Conv_I4\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Call, .+GetOffset.+\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ldelem_I4\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Ret\);"));
    }
    

    private const string ResolvedSystemIndexOpImplicit = @"assembly\.MainModule\.ImportReference\(TypeHelpers\.ResolveMethod\(""System\.Private\.CoreLib"", ""System\.Index"", ""op_Implicit"",System\.Reflection\.BindingFlags\.Default\|System\.Reflection.BindingFlags.Static\|System.Reflection.BindingFlags.Public,"""", ""System.Int32""\)\)";
}
