using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ArrayTests : CecilifierUnitTestBase
{
    [TestCase("string", Code.Ldelem_Ref)]
    [TestCase("C", Code.Ldelem_Ref)]
    [TestCase("S", Code.Ldelem_Any, ", st_S_\\d+")]
    [TestCase("byte", Code.Ldelem_I1)]
    [TestCase("char", Code.Ldelem_I2)]
    [TestCase("int", Code.Ldelem_I4)]
    [TestCase("long", Code.Ldelem_I8)]
    [TestCase("float", Code.Ldelem_R4)]
    [TestCase("double", Code.Ldelem_R8)]
    [TestCase("System.DateTime", Code.Ldelem_Any, ", assembly.MainModule.TypeSystem.DateTime")]
    public void TestAccessStringArray(string elementType, Code code, string operand="")
    {
        var result = RunCecilifier($@"struct S {{}} class C {{ {elementType} M({elementType} []a) => a[2]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(
            cecilifiedCode, 
            Does.Match(
                @"(.+\.Emit\(OpCodes\.)Ldarg_1\);\s+" +
                @"\1Ldc_I4, 2\);\s+" +
                $@"\1{code}{operand}\);"));
    }    
    
    [TestCase("string", Code.Stelem_Ref)]
    [TestCase("C", Code.Stelem_Ref)]
    [TestCase("byte", Code.Stelem_I1)]
    [TestCase("char", Code.Stelem_I2)]
    [TestCase("int", Code.Stelem_I4)]
    [TestCase("long", Code.Stelem_I8)]
    [TestCase("float", Code.Stelem_R4)]
    [TestCase("double", Code.Stelem_R8)]
    [TestCase("System.DateTime", Code.Stelem_Any, ", assembly.MainModule.TypeSystem.DateTime")]
    [TestCase("S", Code.Stelem_Any, @", st_S_\d+")]
    public void TestArrayCreation(string elementType, Code code, string operand="")
    {
        var result = RunCecilifier($@"struct S {{}} class C {{ void M({elementType} value) {{ var data = new[] {{ value }}; }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(
            cecilifiedCode, 
            Does.Match(
                @"(.+\.Emit\(OpCodes\.)Dup\);\s+" +
                @"\1Ldc_I4, 0\);\s+" +
                @"\1Ldarg_1.+\s+" +
                $@"\1{code}{operand}\);\s+"));
    }
}
