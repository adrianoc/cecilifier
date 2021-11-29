using System;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class FieldsTests : CecilifierUnitTestBase
{
    [Test]
    public void TestExternalFields()
    {
        const string code = "class ExternalStaticFieldsAccess { string S() => string.Empty; }";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Contains.Substring("il_S_2.Emit(OpCodes.Ldsfld, assembly.MainModule.ImportReference(TypeHelpers.ResolveField(\"System.String\",\"Empty\")));"));
    }
    
    // [Test]
    // public void TestExternalInstanceFields()
    // {
    //     const string code = "class ExternalInstanceFields { int Instance(System.ValueTuple<int> t) => t.Item1; }";
    //     var result = RunCecilifier(code);
    //     var cecilifiedCode = result.GeneratedCode.ReadToEnd();
    //     
    //     Assert.That(cecilifiedCode, Contains.Substring("XX FORCE IT TO FAIL XX;"));
    // }
}
