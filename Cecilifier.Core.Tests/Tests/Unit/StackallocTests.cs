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
    public void Test_Issue_133_Span_InitializedByStackallocWithSizeFromParameter_PassedAsParameter()
    {
        var result = RunCecilifier(@"using System; class Foo { static void Bar(Span<int> span) {  Bar(stackalloc int[1000]); } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match("""
                                               //Bar\(stackalloc int\[1000\]\);
                                               (?<emit>\s+il_bar_\d+\.Emit\(OpCodes\.)Ldc_I4, 4000\); // 1000 \(elements\) \* 4 \(bytes per element\)
                                               \k<emit>Conv_U\);
                                               \k<emit>Localloc\);
                                               \k<emit>Ldc_I4, 1000\);
                                               \s+var m_declaringType_4 = assembly.MainModule.ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(.+Int32\);
                                               \s+var r_tmpMethod_5 = assembly.MainModule.ImportReference\(m_declaringType_4\.ElementType.+Methods\.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 2 && m.Parameters\[0\].ParameterType.IsByReference == false && m.Parameters\[1\].ParameterType.IsByReference == false\)\);
                                               \s+var r__ctor_6 = new MethodReference\(r_tmpMethod_5.Name, r_tmpMethod_5.ReturnType\)
                                               \s+{
                                               \s+HasThis = r_tmpMethod_5.HasThis,
                                               \s+CallingConvention = r_tmpMethod_5.CallingConvention,
                                               \s+ExplicitThis = r_tmpMethod_5.ExplicitThis,
                                               \s+DeclaringType = m_declaringType_4,
                                               \s+};
                                               \s+r__ctor_6.Parameters.Add\(.+r_tmpMethod_5.Parameters\[0\].+\);
                                               \s+r__ctor_6.Parameters.Add\(.+r_tmpMethod_5.Parameters\[1\].+\);
                                               \k<emit>Newobj, r__ctor_6\);
                                               """));
    }

    [Test]
    public void Test_Issue_133_Span_InitializedByStackallocWithSizeFromField_PassedAsParameter()
    {
        var result = RunCecilifier(@"using System; class Foo { public static int countField; static void Bar(Span<int> span) {  Bar(stackalloc int[countField]); } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        /*var expectedLines = new[]
        {
                "il_bar_3.Emit(OpCodes.Ldsfld, fld_countField_1);", "il_bar_3.Emit(OpCodes.Conv_U);", "il_bar_3.Emit(OpCodes.Sizeof, assembly.MainModule.TypeSystem.Int32);", "il_bar_3.Emit(OpCodes.Mul_Ovf_Un);",
                "il_bar_3.Emit(OpCodes.Localloc);", "il_bar_3.Emit(OpCodes.Ldfld, fld_countField_1);",
                "var l_spanCtor_5 = new MethodReference(\".ctor\", assembly.MainModule.TypeSystem.Void, assembly.MainModule.ImportReference(typeof(System.Span<>)).MakeGenericInstanceType(assembly.MainModule.TypeSystem.Int32)) { HasThis = true };",
                "l_spanCtor_5.Parameters.Add(new ParameterDefinition(\"ptr\", ParameterAttributes.None, assembly.MainModule.TypeSystem.Void.MakePointerType()));",
                "l_spanCtor_5.Parameters.Add(new ParameterDefinition(\"length\", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));",
                "il_bar_3.Emit(OpCodes.Newobj, assembly.MainModule.ImportReference(l_spanCtor_5));", "il_bar_3.Emit(OpCodes.Call, m_bar_2);", "il_bar_3.Emit(OpCodes.Ret);",
            };

        foreach (var expectedLine in expectedLines)
        {
            Assert.That(cecilifiedCode, Contains.Substring(expectedLine));
        }*/
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               //Bar\(stackalloc int\[countField\]\);
                                               (?<emit>\s+il_bar_\d+.Emit\(OpCodes\.)Ldsfld, fld_countField_\d+\);
                                               \k<emit>Conv_U\);
                                               \k<emit>Sizeof, assembly.MainModule.TypeSystem.Int32\);
                                               \k<emit>Mul_Ovf_Un\);
                                               \k<emit>Localloc\);
                                               \k<emit>Ldfld, fld_countField_1\);
                                               \s+var m_declaringType_5 = assembly.MainModule.ImportReference\(.+Span<>.+\).MakeGenericInstanceType\(.+Int32\);
                                               \s+var r_tmpMethod_6 = assembly.MainModule.ImportReference\(m_declaringType_5.ElementType.Resolve\(\).Methods.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 2 && m.Parameters\[0\].ParameterType.IsByReference == false && m.Parameters\[1\].ParameterType.IsByReference == false\)\);
                                               \s+var (?<span_ctor>r__ctor_\d+) = new MethodReference\(r_tmpMethod_6.Name, r_tmpMethod_6.ReturnType\)
                                               \s+{
                                               \s+HasThis = r_tmpMethod_6.HasThis,
                                               \s+CallingConvention = r_tmpMethod_6.CallingConvention,
                                               \s+ExplicitThis = r_tmpMethod_6.ExplicitThis,
                                               \s+DeclaringType = m_declaringType_5,
                                               \s+};
                                               \s+\k<span_ctor>.Parameters.Add\(new ParameterDefinition\(r_tmpMethod_6.Parameters\[0\].Name,.+\)\);
                                               \s+\k<span_ctor>.Parameters.Add\(new ParameterDefinition\(r_tmpMethod_6.Parameters\[1\].Name,.+\)\);
                                               \k<emit>Newobj, \k<span_ctor>\);
                                               """));
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
    
    [Test]
    public void ImplicitStackAllocInNestedExpressions()
    {
        var code = @"using System; class C { void M() => M2(stackalloc [] { 1, 2, 3}); void M2(Span<int> span) {} }";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+var m_declaringType_10 = .+ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(.+Int32\);
                                               \s+var r_tmpMethod_11 = .+ImportReference\(m_declaringType_10.ElementType.Resolve\(\).Methods.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 2 && m.Parameters\[0\].ParameterType.IsByReference == false &&.+\)\);
                                               \s+var (?<span_ctor>r__ctor_\d+) = new MethodReference\(r_tmpMethod_11.Name, r_tmpMethod_11.ReturnType\)
                                               \s+{
                                               \s+HasThis = r_tmpMethod_11.HasThis,
                                               \s+CallingConvention = r_tmpMethod_11.CallingConvention,
                                               \s+ExplicitThis = r_tmpMethod_11.ExplicitThis,
                                               \s+DeclaringType = m_declaringType_10,
                                               \s+};
                                               \s+\k<span_ctor>.Parameters.Add\(new ParameterDefinition\(r_tmpMethod_11.Parameters\[0\].Name,.+\)\);
                                               \s+\k<span_ctor>.Parameters.Add\(new ParameterDefinition\(r_tmpMethod_11.Parameters\[1\].Name,.+\)\);
                                               \s+il_M_5.Emit\(OpCodes.Newobj, \k<span_ctor>\);
                                               """));
    }

    [Test]
    public void ExplicitStackAllocInNestedExpressions()
    {
        var code = @"using System; class C { void M() => M2(stackalloc int[42]); void M2(Span<int> span) {} }";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match("""
                                               //M2\(stackalloc int\[42\]\)
                                               (?<emit>\s+il_M_\d+\.Emit\(OpCodes\.)Ldc_I4, 168\); //.+
                                               \k<emit>Conv_U\);
                                               \k<emit>Localloc\);
                                               \k<emit>Ldc_I4, 42\);
                                               \s+var m_declaringType_6 = .+ImportReference\(typeof\(System.Span<>\)\).MakeGenericInstanceType\(.+Int32\);
                                               \s+var r_tmpMethod_7 = .+ImportReference\(m_declaringType_6.ElementType.Resolve\(\).Methods.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 2 && m.Parameters\[0\].ParameterType.IsByReference == false &&.+\)\);
                                               \s+var (?<span_ctor>r__ctor_\d+) = new MethodReference\(r_tmpMethod_7.Name, r_tmpMethod_7.ReturnType\)
                                               \s+{
                                               \s+HasThis = r_tmpMethod_7.HasThis,
                                               \s+CallingConvention = r_tmpMethod_7.CallingConvention,
                                               \s+ExplicitThis = r_tmpMethod_7.ExplicitThis,
                                               \s+DeclaringType = m_declaringType_6,
                                               \s+};
                                               \s+\k<span_ctor>.Parameters.Add\(.+ParameterDefinition\(r_tmpMethod_7.Parameters\[0\].Name,.+\)\);
                                               \s+\k<span_ctor>.Parameters.Add\(.+ParameterDefinition\(r_tmpMethod_7.Parameters\[1\].Name,.+\)\);
                                               \k<emit>Newobj, \k<span_ctor>\);
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
