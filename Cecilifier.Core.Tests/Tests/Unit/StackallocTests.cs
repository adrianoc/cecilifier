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
    public void Test_Issue_134_Stackalloc_ArrayInitialization_Numeric(string type, string flcTypeName, int sizeofElement, string expectedStindOpCode)
    {
        var code = @$"using System; class Foo {{ unsafe void Bar() {{ {type}* b = stackalloc {type}[] {{ 1, 2 }}; }} }}";
        var result = RunCecilifier(code);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var sizeOfElementSupport = sizeofElement == 1
            ? string.Empty
            : @$"\1Sizeof, assembly.MainModule.TypeSystem.{flcTypeName}\);\s+" +
              @"\1Mul_Ovf_Un\);\s+";

        Assert.That(cecilifiedCode, Does.Match(@$"var l_b_3 = new VariableDefinition\(assembly.MainModule.TypeSystem.{flcTypeName}.MakePointerType\(\)\);\s+"
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
    public void Test_Issue_133_Assign_StackallocToSpan()
    {
        var result = RunCecilifier(@"using System; class Foo { void Bar() { Span<byte> s = stackalloc byte[1000]; } }");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(@".+//Span<byte> s = stackalloc byte\[1000\];\s+" +
                                               @"var l_s_3 = new VariableDefinition\(assembly.MainModule.ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Byte\)\);\s+" +
                                               @"m_bar_1.Body.Variables.Add\(l_s_3\);\s+" +
                                               @"il_bar_2.Emit\(OpCodes.Ldc_I4, 1000\); // 1000 \(elements\) \* 1 \(bytes per element\)\s+" +
                                               @"il_bar_2.Emit\(OpCodes.Conv_U\);\s+" +
                                               @"il_bar_2.Emit\(OpCodes.Localloc\);\s+" +
                                               @"il_bar_2.Emit\(OpCodes.Ldc_I4, 1000\);\s+"));
        Assert.That(cecilifiedCode, Does.Match(
            @"il_bar_2.Emit\(OpCodes.Newobj, assembly.MainModule.ImportReference\(l_spanCtor_4\)\);\s+" +
            @"il_bar_2.Emit\(OpCodes.Stloc, l_s_3\);\s+"));
    }

    [Test]
    public void Test_Issue_133_Span_InitializedByStackallocWithSizeFromParameter_PassedAsParameter()
    {
        var result = RunCecilifier(@"using System; class Foo { static void Bar(Span<int> span) {  Bar(stackalloc int[1000]); } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var expectedLines = new[]
        {
                "il_bar_2.Emit(OpCodes.Ldc_I4, 4000);", "il_bar_2.Emit(OpCodes.Conv_U);", "il_bar_2.Emit(OpCodes.Localloc);", "il_bar_2.Emit(OpCodes.Ldc_I4, 4000);",
                "var l_spanCtor_4 = new MethodReference(\".ctor\", assembly.MainModule.TypeSystem.Void, assembly.MainModule.ImportReference(typeof(System.Span<>)).MakeGenericInstanceType(assembly.MainModule.TypeSystem.Int32)) { HasThis = true };",
                "l_spanCtor_4.Parameters.Add(new ParameterDefinition(\"ptr\", ParameterAttributes.None, assembly.MainModule.ImportReference(typeof(void*))));",
                "l_spanCtor_4.Parameters.Add(new ParameterDefinition(\"length\", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));",
                "il_bar_2.Emit(OpCodes.Newobj, assembly.MainModule.ImportReference(l_spanCtor_4));", "il_bar_2.Emit(OpCodes.Call, m_bar_1);", "il_bar_2.Emit(OpCodes.Ret);",
            };

        foreach (var expectedLine in expectedLines)
        {
            Assert.That(cecilifiedCode, Contains.Substring(expectedLine));
        }
    }

    [Test]
    public void Test_Issue_133_Span_InitializedByStackallocWithSizeFromField_PassedAsParameter()
    {
        var result = RunCecilifier(@"using System; class Foo { public static int countField; static void Bar(Span<int> span) {  Bar(stackalloc int[countField]); } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var expectedLines = new[]
        {
                "il_bar_3.Emit(OpCodes.Ldsfld, fld_countField_1);", "il_bar_3.Emit(OpCodes.Conv_U);", "il_bar_3.Emit(OpCodes.Sizeof, assembly.MainModule.TypeSystem.Int32);", "il_bar_3.Emit(OpCodes.Mul_Ovf_Un);",
                "il_bar_3.Emit(OpCodes.Localloc);", "il_bar_3.Emit(OpCodes.Ldfld, fld_countField_1);",
                "var l_spanCtor_5 = new MethodReference(\".ctor\", assembly.MainModule.TypeSystem.Void, assembly.MainModule.ImportReference(typeof(System.Span<>)).MakeGenericInstanceType(assembly.MainModule.TypeSystem.Int32)) { HasThis = true };",
                "l_spanCtor_5.Parameters.Add(new ParameterDefinition(\"ptr\", ParameterAttributes.None, assembly.MainModule.ImportReference(typeof(void*))));",
                "l_spanCtor_5.Parameters.Add(new ParameterDefinition(\"length\", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));",
                "il_bar_3.Emit(OpCodes.Newobj, assembly.MainModule.ImportReference(l_spanCtor_5));", "il_bar_3.Emit(OpCodes.Call, m_bar_2);", "il_bar_3.Emit(OpCodes.Ret);",
            };

        foreach (var expectedLine in expectedLines)
        {
            Assert.That(cecilifiedCode, Contains.Substring(expectedLine));
        }
    }
    
    [Test]
    public void ImplicitStackAllocInNestedExpressions()
    {
        var code = @"using System; class C { void M() => M2(stackalloc [] { 1, 2, 3}); void M2(Span<int> span) {} }";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"var (l_spanCtor_\d+) = new MethodReference\("".ctor"", assembly.MainModule.TypeSystem.Void, assembly.MainModule.ImportReference\(typeof\(System\.Span<>\)\).MakeGenericInstanceType\(.+TypeSystem.Int32\)\) { HasThis = true };\s+" +
            @"\1.Parameters.Add\(.+""ptr"".+void\*.+\);\s+" +
            @"\1.Parameters.Add\(.+""length"".+TypeSystem.Int32.+\);\s+" +
            @"il_M_\d+.Emit\(OpCodes.Newobj, assembly.MainModule.ImportReference\(\1\)\);"));
    }

    [Test]
    public void ExplicitStackAllocInNestedExpressions()
    {
        var code = @"using System; class C { void M() => M2(stackalloc int[42]); void M2(Span<int> span) {} }";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"var (l_spanCtor_\d+) = new MethodReference\("".ctor"", assembly.MainModule.TypeSystem.Void, assembly.MainModule.ImportReference\(typeof\(System\.Span<>\)\).MakeGenericInstanceType\(.+TypeSystem.Int32\)\) { HasThis = true };\s+" +
            @"\1.Parameters.Add\(.+""ptr"".+void\*.+\);\s+" +
            @"\1.Parameters.Add\(.+""length"".+TypeSystem.Int32.+\);\s+" +
            @"il_M_\d+.Emit\(OpCodes.Newobj, assembly.MainModule.ImportReference\(\1\)\);"));
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
        \s+var (fld_arrayInitializerData_\d+) = new FieldDefinition\("[A-Z0-9]+", FieldAttributes.Assembly \| FieldAttributes.Static \| FieldAttributes.InitOnly, \2\);
        \s+\1.Fields.Add\(\3\);
        \s+\3.InitialValue = Cecilifier.Runtime.TypeHelpers.ToByteArray<Byte>\(stackalloc Byte\[\] { 1, 2, 3 }\);
        \s+//duplicates the top of the stack \(the newly `stackalloced` buffer\) and initialize it from the raw buffer \(\3\).
        \s+(il_.+).Emit\(OpCodes.Dup\);
        \s+\4.Emit\(OpCodes.Ldsflda, \3\);
        """));
    }
}
