using System;
using System.Diagnostics;
using System.IO;
using Mono.Cecil;
using NUnit.Framework;
using NUnit.Framework.Constraints;

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
            Assert.That(cecilifiedCode, Contains.Substring("var enum_nested_1 = new TypeDefinition(\"\", \"Nested\", TypeAttributes.NestedPublic | TypeAttributes.Sealed, assembly.MainModule.ImportReference(typeof(System.Enum)));"), cecilifiedCode);
            Assert.That(cecilifiedCode, Contains.Substring("var l_first_3 = new FieldDefinition(\"First\", FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.Public | FieldAttributes.HasDefault, enum_nested_1) { Constant = 42 } ;"), cecilifiedCode);
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
            Assert.That(cecilifiedCode, Does.Match("il_method_21.Emit\\(OpCodes.Callvirt, .*\"System.Diagnostics.Process\", \"System.Diagnostics.Process\", \"Kill\".+"), cecilifiedCode);
            Assert.That(cecilifiedCode, Does.Match("il_property_24.Emit\\(OpCodes.Callvirt, .+\"System.Diagnostics.Process\", \"System.Diagnostics.Process\", \"get_ProcessName\".+"), cecilifiedCode);
        }
    }
}
