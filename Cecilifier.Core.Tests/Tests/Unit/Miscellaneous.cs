using System;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class MiscellaneousTests : CecilifierUnitTestBase
    {
        [Test]
        public void Test_Issue110()
        {
            var code = "class Foo { void Bar(string s) { var index = 2; Bar(s.Substring(0, index)); } }";
            var expectedSnippet = @".*(?<emit>.+\.Emit\(OpCodes\.)Ldarg_0\);\s"
                                  + @"\1Ldarg_1.+;\s"
                                  + @"\1Ldc_I4, 0.+;\s"
                                  + @"\1Ldloc, l_index_4.+;\s"
                                  + @"\1Callvirt, .+""Substring"".+;\s"
                                  + @"\1Call, m_bar_1.+;\s"
                                  + @"\1Ret.+;";

            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }

        [Test]
        public void CustomDelegate_ComparisonToNull_GeneratesCeqInstruction_InsteadOfCalling_Operator()
        {
            var code = @"using System;
public delegate object CustomDelegate(int arg);
public static class ObjectMaker
{
	public static bool Test(CustomDelegate cd) => cd == null;
}";
            var expectedCecilifiedCode = @"il_test_10.Emit\(OpCodes.Ldarg_0\).+\s+" +
                                         @"il_test_10.Emit\(OpCodes.Ldnull\).+\s+" +
                                         @"il_test_10.Emit\(OpCodes.Ceq\).+\s+" +
                                         @"il_test_10.Emit\(OpCodes.Ret\);";

            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match(expectedCecilifiedCode), cecilifiedCode);
        }

        [Test]
        public void Nested_Enum()
        {
            var code = @"
public static class Outer
{
	public enum Nested { First = 42 }
}";
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode,
                Contains.Substring("var enum_nested_1 = new TypeDefinition(\"\", \"Nested\", TypeAttributes.NestedPublic | TypeAttributes.Sealed, assembly.MainModule.ImportReference(typeof(System.Enum)));"),
                cecilifiedCode);
            Assert.That(cecilifiedCode,
                Contains.Substring(
                    "var l_first_3 = new FieldDefinition(\"First\", FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.Public | FieldAttributes.HasDefault, enum_nested_1) { Constant = 42 } ;"),
                cecilifiedCode);
        }

        [Test]
        public void CompoundStatement_WithBraceInSameLine_GeneratesValidComments()
        {
            var code = @"
using static System.Console;
public class Foo
{
	void Bar(int i) { WriteLine(i); }

	void BarBaz(int i) 
    {
        if (i > 42) {
            WriteLine(i);
        }
    }
}";
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode, Contains.Substring("//Parameters of 'void Bar(int i) '"));
            Assert.That(cecilifiedCode, Contains.Substring("//if (i > 42) "));
        }

        [Test]
        public void Test_Issue_126_Types()
        {
            var result = RunCecilifier(@"
                    using System.IO;
                    namespace NS { public struct FileStream { } }

                    class Foo 
                    {
                         public FileStream file; // System.IO.FileStream since NS.FileStream is not in scope here. 
                         public NS.FileStream definedInFooBar; 
                    }");

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.False(cecilifiedCode.Contains("FieldDefinition(\"file\", FieldAttributes.Public, st_fileStream_0);"), cecilifiedCode);
            Assert.True(cecilifiedCode.Contains("FieldDefinition(\"definedInFooBar\", FieldAttributes.Public, st_fileStream_0);"), cecilifiedCode);
            Assert.True(cecilifiedCode.Contains("FieldDefinition(\"file\", FieldAttributes.Public, assembly.MainModule.ImportReference(typeof(FileStream)));"), cecilifiedCode);
        }

        [Test]
        public void Test_Issue_126_Members()
        {
            var result = RunCecilifier(@"
                    using System;
                    using System.Diagnostics;
                    namespace NS 
                    {
                        public struct Process { public void Kill() { } public string ProcessName => """"; public event EventHandler Exited;}
                    }

                    class Foo 
                    {
                        public Process proc;
                        private void Handler(object sender, EventArgs args) { } 

                        void Method() => proc.Kill();
                        string Property() => proc.ProcessName;
                        // void Event() => proc.Exited += Handler; // Depends on issue # 127
                    }");

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match("il_method_21.Emit\\(OpCodes.Callvirt, .*\"System.Diagnostics.Process, System.Diagnostics.Process\", \"Kill\".+"), cecilifiedCode);
            Assert.That(cecilifiedCode, Does.Match("il_property_24.Emit\\(OpCodes.Callvirt, .+\"System.Diagnostics.Process, System.Diagnostics.Process\", \"get_ProcessName\".+"), cecilifiedCode);
        }

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
                                                   @"il_bar_2.Emit\(OpCodes.Ldc_I4, 1000\);\s+" +
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
                "var l_spanCtor_5 = new MethodReference(\".ctor\", assembly.MainModule.TypeSystem.Void, assembly.MainModule.ImportReference(typeof(Span<>)).MakeGenericInstanceType(assembly.MainModule.TypeSystem.Int32)) { HasThis = true };",
                "l_spanCtor_5.Parameters.Add(new ParameterDefinition(\"ptr\", ParameterAttributes.None, assembly.MainModule.ImportReference(typeof(void*))));",
                "l_spanCtor_5.Parameters.Add(new ParameterDefinition(\"length\", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));",
                "il_bar_2.Emit(OpCodes.Newobj, assembly.MainModule.ImportReference(l_spanCtor_5));", "il_bar_2.Emit(OpCodes.Call, m_bar_1);", "il_bar_2.Emit(OpCodes.Ret);",
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
                "var l_spanCtor_6 = new MethodReference(\".ctor\", assembly.MainModule.TypeSystem.Void, assembly.MainModule.ImportReference(typeof(Span<>)).MakeGenericInstanceType(assembly.MainModule.TypeSystem.Int32)) { HasThis = true };",
                "l_spanCtor_6.Parameters.Add(new ParameterDefinition(\"ptr\", ParameterAttributes.None, assembly.MainModule.ImportReference(typeof(void*))));",
                "l_spanCtor_6.Parameters.Add(new ParameterDefinition(\"length\", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));",
                "il_bar_3.Emit(OpCodes.Newobj, assembly.MainModule.ImportReference(l_spanCtor_6));", "il_bar_3.Emit(OpCodes.Call, m_bar_2);", "il_bar_3.Emit(OpCodes.Ret);",
            };

            foreach (var expectedLine in expectedLines)
            {
                Assert.That(cecilifiedCode, Contains.Substring(expectedLine));
            }
        }

        [Test]
        public void Test_Issue_133_Assign_StackallocToSpan_WithInitializer()
        {
            const string code = @"using System; class Foo { void Bar() { Span<char> s = stackalloc char[] { 'A', 'G', 'C', 'G' } ; } }";
            var result = RunCecilifier(code);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match(
                @".+(il_bar_2\.Emit\(OpCodes\.)Ldc_I4, 4\);\s+" +
                @"var l_spanSizeInBytes_4 = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);\s+" +
                @"m_bar_1.Body.Variables.Add\(l_spanSizeInBytes_4\);\s+" +
                @"\1Stloc, l_spanSizeInBytes_4\);\s+" +
                @"\1Ldloc, l_spanSizeInBytes_4\);\s+" +
                @"\1Conv_U\);\s+" +
                @"\1Sizeof, assembly.MainModule.TypeSystem.Char\);\s+" +
                @"\1Mul_Ovf_Un\);\s+" +
                @"\1Localloc\);\s+" +
                @"\1Dup\);\s+" +
                @"\1Ldc_I4, 65\);\s+" +
                @"\1Stind_I2\);\s+" +
                @"\1Dup\);\s+" +
                @"\1Ldc_I4, 2\);\s+" +
                @"\1Add\);\s+" +
                @"\1Ldc_I4, 71\);\s+" +
                @"\1Stind_I2\);\s+" +
                @"\1Dup\);\s+" +
                @"\1Ldc_I4, 4\);\s+" +
                @"\1Add\);\s+" +
                @"\1Ldc_I4, 67\);\s+" +
                @"\1Stind_I2\);\s+" +
                @"\1Dup\);\s+" +
                @"\1Ldc_I4, 6\);\s+" +
                @"\1Add\);\s+" +
                @"\1Ldc_I4, 71\);\s+" +
                @"\1Stind_I2\);\s+" +
                @"\1Ldloc, l_spanSizeInBytes_4\);\s+"));
        }
        
        [TestCase("class StaticFieldAddress { static int field; static void M1(ref int i) { } static void M() => M1(ref field); }", "Ldsflda", TestName="StaticPassingByRef")]
        [TestCase("class FieldAddress { int field; void M1(ref int i) { } void M() => M1(ref field); }", "Ldflda", TestName="PassingByRef")]
        [TestCase("class FieldAddress { int field; unsafe void Fixed() { fixed(int *p = &field) { } } }", "Ldflda", TestName="Fixed")]
        [TestCase("class StaticFieldAddress { static int field; static unsafe void Fixed() { fixed(int *p = &field) { } } }", "Ldsflda", TestName="StaticFixed")]
        public void Field_Address(string code, string expectedLoadOpCode)
        {
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Contains.Substring(expectedLoadOpCode));
        }
        
        [TestCase("fieldDelegate")]
        [TestCase("PropertyDelegate")]
        [TestCase("localDelegate")]
        [TestCase("paramDelegate")]
        [TestCase("fieldFunction")]
        [TestCase("PropertyFunction")]
        [TestCase("localFunction")]
        [TestCase("paramFunction")]
        public void LdindTests(string varToUse)
        {
            var code = 
                $@"
            using System;
            unsafe class Foo 
            {{ 
                delegate*<bool, int, void> fieldFunction;                
                Action<bool, int> fieldDelegate;

                delegate*<bool, int, void> PropertyFunction {{ get; set; }}
                Action<bool, int> PropertyDelegate {{ get; set; }}

                void Bar(Action<bool, int> paramDelegate, delegate*<bool, int, void> paramFunction, int v)
                {{
                    Action<bool, int> localDelegate = paramDelegate;
                    delegate*<bool, int, void> localFunction = paramFunction; 
                    {varToUse}(true, v);
                }}
            }}";
        
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Not.Contains("Ldind"));
        }        
    }
}
