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
    [TestCase("short", Code.Ldelem_I2)]
    [TestCase("int", Code.Ldelem_I4)]
    [TestCase("long", Code.Ldelem_I8)]
    [TestCase("float", Code.Ldelem_R4)]
    [TestCase("double", Code.Ldelem_R8)]
    [TestCase("System.DateTime", Code.Ldelem_Any, ", assembly.MainModule.TypeSystem.DateTime")]
    public void TestAccessStringArray(string elementType, Code code, string operand = "")
    {
        var result = RunCecilifier($@"struct S {{}} class C {{ {elementType} M({elementType} []a) => a[2]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(
                $"""
                      (.+\.Emit\(OpCodes\.)Ldarg_1\);
                      \1Ldc_I4, 2\);
                      \1{code}{operand}\);
                      """));
    }

    [TestCase("string", Code.Stelem_Ref)]
    [TestCase("C", Code.Stelem_Ref)]
    [TestCase("byte", Code.Stelem_I1)]
    [TestCase("char", Code.Stelem_I2)]
    [TestCase("short", Code.Stelem_I2)]
    [TestCase("int", Code.Stelem_I4)]
    [TestCase("long", Code.Stelem_I8)]
    [TestCase("float", Code.Stelem_R4)]
    [TestCase("double", Code.Stelem_R8)]
    [TestCase("System.DateTime", Code.Stelem_Any, ", assembly.MainModule.TypeSystem.DateTime")]
    [TestCase("S", Code.Stelem_Any, @", st_S_\d+")]
    public void TestArrayInstantiation(string elementType, Code code, string operand = "")
    {
        var result = RunCecilifier($@"struct S {{}} class C {{ void M({elementType} value) {{ var data = new[] {{ value }}; }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(
                $"""
                      (.+\.Emit\(OpCodes\.)Dup\);
                      \1Ldc_I4, 0\);
                      \1Ldarg_1.+
                      \1{code}{operand}\);\s+
                      """));
    }

    [TestCase("System.String")]
    [TestCase("C", @"cls_C_\d+")]
    [TestCase("System.Byte")]
    [TestCase("System.Char")]
    [TestCase("System.Int16")]
    [TestCase("System.Int32")]
    [TestCase("System.Int64")]
    [TestCase("System.Single")]
    [TestCase("System.Double")]
    [TestCase("System.DateTime")]
    [TestCase("S", @"st_S_\d+")]
    public void TestJaggedArrayInstantiation(string elementType, string operand = null)
    {
        var result = RunCecilifier($@"struct S {{}} class C {{ void M({elementType} value) {{ var data = new {elementType}[42][]; }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var operandTypeMatch = operand ?? $"assembly.MainModule.Type{elementType}";

        Assert.That(cecilifiedCode, Does.Match($@"new VariableDefinition\({operandTypeMatch}.MakeArrayType\(\).MakeArrayType\(\)\);\s+"));

        Assert.That(
            cecilifiedCode,
            Does.Match(
                $"""
                (.+\.Emit\(OpCodes\.)Ldc_I4, 42\);
                \1Newarr, {operandTypeMatch}.MakeArrayType\(\)\);\s+
                """));
    }

    [TestCase("string", Code.Stelem_Ref)]
    [TestCase("C", Code.Stelem_Ref)]
    [TestCase("byte", Code.Stelem_I1)]
    [TestCase("char", Code.Stelem_I2)]
    [TestCase("short", Code.Stelem_I2)]
    [TestCase("int", Code.Stelem_I4)]
    [TestCase("long", Code.Stelem_I8)]
    [TestCase("float", Code.Stelem_R4)]
    [TestCase("double", Code.Stelem_R8)]
    [TestCase("System.DateTime", Code.Stelem_Any, ", assembly.MainModule.TypeSystem.DateTime")]
    [TestCase("S", Code.Stelem_Any, @", st_S_\d+")]
    public void TestJaggedArrayAssignment(string elementType, Code code, string operand = "")
    {
        var result = RunCecilifier($@"struct S {{}} class C {{ void M({elementType} [][]array, {elementType} value) {{ array[0][1] = value; }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(
                $"""
                       //array\[0\]\[1\] = value;
                       (.+Emit\(OpCodes\.)Ldarg_1\);
                       \1Ldc_I4, 0.+
                       \1Ldelem_Ref.+
                       \1Ldc_I4, 1.+
                       \1Ldarg_2.+
                       \1{code}{operand}.+;
                       """));
    }

    [TestCase("class Foo { void M() { int []intArray = { 1 }; } }", TestName = "Local Variable")]
    [TestCase("class Foo { int []intArray = { 1 }; }", TestName = "Field")]
    public void TestImplicitArrayInitializationUnoptimized(string code)
    {
        // See comment in TestImplicitArrayInitializationOptimized 
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode,
            Does.Match(
         """
               ((?:.+)\.Emit\(OpCodes\.)Ldc_I4, 1\);
               \1Newarr, assembly.MainModule.TypeSystem.Int32\);
               \1Dup\);
               \1Ldc_I4, 0\);
               \1Ldc_I4, 1\);
               \1Stelem_I4\);
               \1(Stfld|Stloc), .+_intArray_\d+\);
               """));
    }

    [TestCase("class Foo { void M() { int []intArray = { 1, 2, 3 }; } }", TestName = "Local Variable")]
    [TestCase("class Foo { int []intArray = { 1, 2, 3 }; }", TestName = "Field")]
    public void TestImplicitArrayInitializationOptimized(string code)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        // see comment in ExpressionVisitor.VisitInitializerExpression()
        Assert.That(cecilifiedCode, Contains.Substring($"Note that as of Cecilifier version {typeof(Cecilifier).Assembly.GetName().Version} the generated code will differ from the"));
        Assert.That(cecilifiedCode, Contains.Substring("C# compiler one since Cecilifier does not apply some optimizations."));
    }
}
