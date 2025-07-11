using System;
using System.Linq;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NSubstitute;
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
        public void Test_Issue_126_Types()
        {
            var result = RunCecilifier(@"
                    using System.IO;
                    namespace NS { public struct FileStream { } }

                    class Foo 
                    {
                         public FileStream file; // System.IO.FileStream since NS.FileStream is not in scope here. 
                         public NS.FileStream definedInNS; 
                    }");

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode.Contains("FieldDefinition(\"file\", FieldAttributes.Public, st_fileStream_0);"), Is.False, cecilifiedCode);
            Assert.That(cecilifiedCode,Does.Match("""var st_fileStream_0 = new TypeDefinition\("NS", "FileStream", .+\);"""));
            Assert.That(cecilifiedCode.Contains("FieldDefinition(\"definedInNS\", FieldAttributes.Public, st_fileStream_0);"), Is.True, cecilifiedCode);
            Assert.That(cecilifiedCode.Contains("FieldDefinition(\"file\", FieldAttributes.Public, assembly.MainModule.ImportReference(typeof(System.IO.FileStream)));"), Is.True, cecilifiedCode);
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
            Assert.That(cecilifiedCode, Does.Match("""il_method_\d+.Emit\(OpCodes.Callvirt, .*typeof\(System.Diagnostics.Process\), "Kill".+"""), cecilifiedCode);
            Assert.That(cecilifiedCode, Does.Match(@"il_property_\d+.Emit\(OpCodes.Callvirt, .+typeof\(System.Diagnostics.Process\), ""get_ProcessName"".+"), cecilifiedCode);
        }

        [TestCase("class StaticFieldAddress { static int field; static void M1(ref int i) { } static void M() => M1(ref field); }", "Ldsflda", TestName = "StaticPassingByRef")]
        [TestCase("class FieldAddress { int field; void M1(ref int i) { } void M() => M1(ref field); }", "Ldflda", TestName = "PassingByRef")]
        [TestCase("class FieldAddress { int field; unsafe void Fixed() { fixed(int *p = &field) { } } }", "Ldflda", TestName = "Fixed")]
        [TestCase("class StaticFieldAddress { static int field; static unsafe void Fixed() { fixed(int *p = &field) { } } }", "Ldsflda", TestName = "StaticFixed")]
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

        [Test]
        public void TestEvents()
        {
            var source = @"class Foo {
                    private void Handler(object sender, System.EventArgs args) { }
                    void SetEvent(System.Diagnostics.Process proc) => proc.Exited += Handler;
                }";

            var result = RunCecilifier(source);

            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(
                @"il_setEvent_6.Emit\(OpCodes.Ldarg_1\);
			il_setEvent_6.Emit\(OpCodes.Ldarg_0\);
			il_setEvent_6.Emit\(OpCodes.Ldftn, m_handler_1\);
			il_setEvent_6.Emit\(OpCodes.Newobj,.+System.EventHandler.+,.+System.Object.+,.+System.IntPtr.+\);
			il_setEvent_6.Emit\(OpCodes.Callvirt,.+add_Exited.+\);"));
        }

        [Test]
        public void TypesNotInSystemCorelib_AreResolved()
        {
            var source = @"class Foo {
                    private void Handler(object sender, System.ConsoleCancelEventArgs args) { }
                    void SetEvent() => System.Console.CancelKeyPress += Handler;
                }";

            var result = RunCecilifier(source);
            Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring("typeof(System.Console), \"add_CancelKeyPress\","));
        }

        [TestCase("j")]
        [TestCase("j + 2")]
        [TestCase("j > 1 ? 0 : 1")]
        public void TestCallerArgumentExpressionAttribute(string expression)
        {
            var source = $@"class Foo {{ void M(int i, [System.Runtime.CompilerServices.CallerArgumentExpression(""i"")] string s = null) {{ }} void Call(int j) => M({expression}); }}";

            var result = RunCecilifier(source);
            Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring($"Ldstr, \"{expression}\""));
        }

        [TestCase("\"foo\"")]
        [TestCase("null")]
        public void TestCallerArgumentExpressionAttribute_InvalidParameterName(string defaultParameterValue)
        {
            var source = $@"class Foo {{ void M(int i, [System.Runtime.CompilerServices.CallerArgumentExpression(""DoNotExist"")] string s = {defaultParameterValue}) {{ }} void Call(int j) => M(j); }}";

            var result = RunCecilifier(source);

            if (defaultParameterValue == "null")
                Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring("Ldnull"));
            else
                Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring($"Ldstr, {defaultParameterValue}"));
        }

        [TestCase(
            "System.DateTime d = new();",
            """
            (il_topLevelMain_\d+).Emit\(OpCodes.Ldloca_S, l_d_\d+\);
            \s+\1.Emit\(OpCodes.Initobj, .+ImportReference\(typeof\(System.DateTime\)\)\);
            """,
            TestName = "Value Type")]

        [TestCase("object o = new();", """il_topLevelMain_\d+.Emit\(OpCodes.Newobj,.+typeof\(System.Object\), ".ctor",.+\);""", TestName = "Simplest")]
        [TestCase(
            "C c = new(42); class C { public C(int i) {} }",
                    """
                    var ctor_C_\d+ = new MethodDefinition\(".ctor", .+, assembly.MainModule.TypeSystem.Void\);
                    """,
                    """
                    var (p_i_\d+) = new ParameterDefinition\("i", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32\);
                    \s+ctor_C_\d+.Parameters.Add\(\1\);
                    """,
                    """
                    \s+il_topLevelMain_7.Emit\(OpCodes.Ldc_I4, 42\);
                    \s+il_topLevelMain_7.Emit\(OpCodes.Newobj, ctor_C_\d+\);
                    """,
            TestName = "Constructor")]
        [TestCase(
            "C c = new() { Value = 42 }; class C { public int Value; }",
            """
                    (il_topLevelMain_\d+.Emit\(OpCodes\.)Newobj, ctor_C_\d+\);
                    \s+\1Dup\);
                    \s+\1Ldc_I4, 42\);
                    \s+\1Stfld, fld_value_\d+\);
                    \s+\1Stloc, l_c_\d+\);
                    """,
            TestName = "Object Initializer")]
        public void TestImplicitObjectCreation(string code, params string[] expectations)
        {
            var r = RunCecilifier(code);
            var cecilifiedCode = r.GeneratedCode.ReadToEnd();
            foreach(var expected in expectations)
            {
                Assert.That(cecilifiedCode, Does.Match(expected));
            }
        }

        [Test]
        public void CecilifierDiagnostic_IsMapped_FromCompilerDiagnostic([Values] DiagnosticSeverity severity)
        {
            var location = Substitute.For<Location>();
            location.GetLineSpan().Returns(new FileLinePositionSpan());
                
            var compilerDiagnostic = Substitute.For<Diagnostic>();
            compilerDiagnostic.Id.Returns("foo");
            compilerDiagnostic.Severity.Returns(severity);
            compilerDiagnostic.Location.Returns(location);
            
            var cecilifierDiagnostic = CecilifierDiagnostic.FromCompiler(compilerDiagnostic);
            Assert.That(cecilifierDiagnostic.Kind, Is.EqualTo(severity switch
            {
                DiagnosticSeverity.Hidden => DiagnosticKind.Information,
                DiagnosticSeverity.Info => DiagnosticKind.Information,
                DiagnosticSeverity.Error => DiagnosticKind.Error,
                DiagnosticSeverity.Warning => DiagnosticKind.Warning,
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
            }));
        }

        [TestCase("""
                  public class DeclaringClass 
                  {
                      public DeclaringClass() {}
                      public int publicField;
                      public string PublicProperty { get; set; }
                      public event System.Action PublicEvent;
                      public void PublicMethod() { }
                      public int this[int i] => i;
                      public int this[string s] => s.Length;
                      public static int operator+(DeclaringClass d1, DeclaringClass d2) => 0;
                      
                      private int privateField;
                      private string PrivateProperty { get; set; }
                      private event System.Action PrivateEvent;
                      private void PrivateMethod() { }
                      
                      public void InnerClass() { }
                  }
                  """, TestName = "Class")]
        [TestCase("""
                  public struct DeclaringStruct 
                  {
                      public int publicField;
                      public string PublicProperty { get; set; }
                      public event System.Action PublicEvent;
                      public void PublicMethod() { }
                      public int this[int i] => i;
                      public int this[string s] => s.Length;
                      public static int operator+(DeclaringClass d1, DeclaringClass d2) => 0;
                  }
                  """, TestName = "Struct")]
        [TestCase("""
                  public interface IDeclaringInterface 
                  {
                      string Property { get; set; }
                      event System.Action Event;
                      void Method() { }
                  }
                  """, TestName = "Interface")]
        
        [TestCase("""
                  public interface IDeclaringInterface<T> where T : IDeclaringInterface<T> 
                  {
                      static T Zero { get; }
                      static T operator+(T d1, T d2); 
                  }
                  """, TestName = "Interface2")]
        
        [TestCase("""
                  public enum DeclaringEnum 
                  {
                    None = 0,
                    Value1 = 1
                  }
                  """, TestName = "Enums")]
        public void TypeDeclarationResolverTests(string code)
        {
            var st = CSharpSyntaxTree.ParseText(code);
            
            var resolver = new TypeDeclarationResolver();
            var parentType = st.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().First();
            var memberDeclarationSyntaxes = st.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>();
            foreach (var syntax in memberDeclarationSyntaxes)
            {
                Assert.That(resolver.Resolve(syntax), Is.SameAs(parentType), syntax.ToString());
            }
        }
        
        [TestCase("var f = (int i) => i + 1;")]
        [TestCase("System.Func<int, int> f = (int i) => i + 1;")]
        public void SimpleLambda_ToDelegateConversion_DoesNotCrash(string code)
        {
            var ctx = RunCecilifier(code);
            Assert.Pass();
        }

        public class RecordTests : CecilifierUnitTestBase
        {
            [TestCase("class", TestName = "NullableContext and NullableAttribute are added to the type definition - class")]
            [TestCase("struct", TestName = "NullableContext and NullableAttribute are added to the type definition - struct")]
            public void NullableContextAndNullableAttributes(string kind)
            {
                var r = RunCecilifier($"public record {kind} RecordTest;");
                var cecilifiedCode = r.GeneratedCode.ReadToEnd();
                Assert.That(cecilifiedCode, Does.Match("""
                                                       (attr_nullableContext_\d+).ConstructorArguments.Add\(new CustomAttributeArgument\(assembly.MainModule.TypeSystem.Int32, 1\)\);
                                                       \s+rec_recordTest_\d+.CustomAttributes.Add\(\1\);
                                                       """));

                Assert.That(cecilifiedCode, Does.Match("""
                                                       (attr_nullable_\d+).ConstructorArguments.Add\(new CustomAttributeArgument\(assembly.MainModule.TypeSystem.Int32, 0\)\);
                                                       \s+rec_recordTest_\d+.CustomAttributes.Add\(\1\);
                                                       """));
            }
        }
    }
}
