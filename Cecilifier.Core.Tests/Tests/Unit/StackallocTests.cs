using System;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class StackallocTests : CecilifierUnitTestBase
{
    [Test]
    public void Test_Issue_134_Stackalloc_ArrayInitialization_Character()
    {
        var code = @"using System; class Foo { unsafe void Bar() { char* ch = stackalloc char[] { 'A', 'V' }; } }";
        var result = RunCecilifier(code);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Contains.Substring("Stind_I2"));
        Assert.That(cecilifiedCode, Contains.Substring("Sizeof, assembly.MainModule.TypeSystem.Char"));
    }

    [Test]
    public void Test_Issue_134_Stackalloc_ArrayInitialization_Boolean()
    {
        var code = @"using System; class Foo { unsafe void Bar() { bool* bp = stackalloc bool[] { true, false }; } }";
        var result = RunCecilifier(code);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Contains.Substring("Stind_I1"));
        Assert.That(cecilifiedCode, Does.Not.Contains("Sizeof, assembly.MainModule.TypeSystem.Boolean"));
    }

    [TestCase("byte", nameof(Byte), sizeof(byte), "Stind_I1", TestName = "byte")]
    [TestCase("sbyte", nameof(SByte), sizeof(sbyte), "Stind_I1", TestName = "sbyte")]
    [TestCase("int", nameof(Int32), sizeof(int), "Stind_I4", TestName = "int")]
    [TestCase("uint", nameof(UInt32), sizeof(uint), "Stind_I4", TestName = "uint")]
    [TestCase("short", nameof(Int16), sizeof(short), "Stind_I2", TestName = "short")]
    [TestCase("ushort", nameof(UInt16), sizeof(ushort), "Stind_I2", TestName = "ushort")]
    [TestCase("long", nameof(Int64), sizeof(long), "Stind_I8", TestName = "long")]
    [TestCase("ulong", nameof(UInt64), sizeof(ulong), "Stind_I8", TestName = "ulong")]
    
    [TestCase("double", nameof(Double), sizeof(double), "Stind_R8", TestName = nameof(Double))]
    [TestCase("float", nameof(Single), sizeof(Single), "Stind_R4", TestName = nameof(Single))]
    public void Test_Issue_134_Stackalloc_ArrayInitialization_Numeric(string type, string fclTypeName, int sizeofElement, string expectedStindOpCode)
    {
        var code = @$"using System; class Foo {{ unsafe void Bar() {{ {type}* b = stackalloc {type}[] {{ 1, 2 }}; }} }}";
        var result = RunCecilifier(code);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var sizeOfElementSupport = sizeofElement == 1
            ? string.Empty
            : @$"\1Sizeof, assembly.MainModule.TypeSystem.{fclTypeName}\);\s+" +
              @"\1Mul_Ovf_Un\);\s+";

        Assert.That(cecilifiedCode, Does.Match(@$"var l_b_3 = new VariableDefinition\(assembly.MainModule.TypeSystem.{fclTypeName}.MakePointerType\(\)\);\s+"
                                               + @"m_bar_1.Body.Variables.Add\(l_b_3\);\s+"
                                               + @"(.+\.Emit\(OpCodes\.)Ldc_I4, 2\);\s+"
                                               + @"\1Conv_U\);\s+"
                                               + sizeOfElementSupport
                                               + @"\1Localloc\);\s+"
                                               + @"\1Dup\);\s+"
                                               + @"\1Ldc_I4, 1\);\s+"
                                               + @$"\1{expectedStindOpCode}\);\s+"
                                               + @"\1Dup\);\s+"
                                               + @$"\1Ldc_I4, {sizeofElement}\);\s+"
                                               + @"\1Add\);\s+"
                                               + @"\1Ldc_I4, 2\);\s+"
                                               + @$"\1{expectedStindOpCode}\);\s+"
                                               + @"\1Stloc, l_b_3\);\s+"
                                               + @"\1Ret\);\s+"));
    }
    
    [Test]
    public void Test_Span_InitializedByStackallocWithSizeFromProperty()
    {
        var result = RunCecilifier("using System; class Foo { public static int CountProperty => 42; static void Bar(Span<int> span) {  Bar(stackalloc int[CountProperty]); } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               //Bar\(stackalloc int\[CountProperty\]\);
                                               (?<ilp>\s+il_bar_\d+\.Emit\(OpCodes\.)Call, m_get_2\);
                                               \s+var (?<span_count>l_spanElementCount_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);
                                               \s+m_bar_\d+.Body\.Variables\.Add\(\k<span_count>\);
                                               \k<ilp>Stloc, \k<span_count>\);
                                               \k<ilp>Ldloc, \k<span_count>\);
                                               """));

    }
    
    [TestCase("paramCount", "Ldarg_0", TestName = "Parameter")]
    [TestCase("localCount", @"Ldloc, l_localCount_\d+", TestName = "Local Variable")]
    public void Test_Span_InitializedByStackallocWithSizeFromLocalVariableOrParameter(string memberToUse, string storageLoadOpCodeAndArgument)
    {
        var result = RunCecilifier($"using System; class Foo {{ static void Bar(int paramCount) {{ int localCount = paramCount; Span<int> span = stackalloc int[{memberToUse}]; }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match($"""
                                               //Span<int> span = stackalloc int\[{memberToUse}\];
                                               \s+var (?<span_var>l_span_\d+) = new VariableDefinition\(.+ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(.+Int32\)\);
                                               \s+m_bar_\d+\.Body.Variables.Add\(\k<span_var>\);
                                               (?<ilp>\s+il_bar_\d+\.Emit\(OpCodes\.){storageLoadOpCodeAndArgument}\);
                                               \k<ilp>Conv_U\);
                                               """));
    }
    
    [TestCase("System.Span<byte>")]
    [TestCase("var")]
    public void InitializationOptimized(string storageType)
    {
        var result = RunCecilifier($$"""unsafe class Foo { void M() { {{storageType}} local = stackalloc byte[] { 1, 2, 3 }; } }""");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match("""
        \s+//<PrivateImplementationDetails> class.
        \s+//This type is emitted by the compiler.
        \s+var (cls_privateImplementationDetails_\d+) = new TypeDefinition\("", "<PrivateImplementationDetails>", TypeAttributes.NotPublic \| TypeAttributes.Sealed \| TypeAttributes.AnsiClass \| TypeAttributes.AutoLayout, assembly.MainModule.TypeSystem.Object\);
        \s+assembly.MainModule.Types.Add\(\1\);
        \s+//__StaticArrayInitTypeSize=3 struct.
        \s+//This struct is emitted by the compiler and is used to hold raw data used in arrays/span initialization optimizations
        \s+var (st_rawDataTypeVar_\d+) = new TypeDefinition\("", "__StaticArrayInitTypeSize=3", TypeAttributes.NestedAssembly \| TypeAttributes.Sealed \| TypeAttributes.AnsiClass \| TypeAttributes.ExplicitLayout.+\) \{ ClassSize = 3,PackingSize = 1 \};
        \s+\1.NestedTypes.Add\(\2\);
        \s+var (fld_arrayInitializerData_\d+) = new FieldDefinition\("[A-Z0-9]+", FieldAttributes.Assembly \| FieldAttributes.Static \| FieldAttributes.InitOnly | FieldAttributes.HasFieldRVA, \2\);
        \s+\1.Fields.Add\(\3\);
        \s+\3.InitialValue = \[ 0x01,0x02,0x03, \];
        \s+//duplicates the top of the stack \(the newly `stackalloced` buffer\) and initialize it from the raw buffer \(\3\).
        \s+(il_.+).Emit\(OpCodes.Dup\);
        \s+\4.Emit\(OpCodes.Ldsflda, \3\);
        """));
    }
}
