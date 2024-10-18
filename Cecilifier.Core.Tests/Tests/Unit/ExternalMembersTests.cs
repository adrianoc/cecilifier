using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class ExternalMembersTests : CecilifierUnitTestBase
    {
        [Test]
        public void NoDllImport()
        {
            var result = RunCecilifier("public class C { public extern int M(); int Call() => M(); }");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            var match = Regex.Match(cecilifiedCode, "var (?<methodVar>.+) = new MethodDefinition\\(\"M\"");
            Assert.That(match.Success, Is.True);

            var methodVarName = match.Groups["methodVar"].Captures[0].Value;

            Assert.That(methodVarName, Is.Not.Empty);
            Assert.That(cecilifiedCode, Does.Not.Contains("new PInvokeInfo("));
            Assert.That(cecilifiedCode, Does.Not.Contains($"{methodVarName}.Body"));
        }

        [Test]
        public void TestPreserveSig([Values] bool preserveSig)
        {
            var result = RunCecilifier($"using System.Runtime.InteropServices; public class C {{ [DllImport(\"Foo\", PreserveSig = {preserveSig.ToString().ToLower()})] public static extern int M(); int Call() => M(); }}");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode.Contains("MethodImplAttributes.PreserveSig"), Is.EqualTo(preserveSig));
        }

        [Test]
        public void TestCallingConvention([Values] CallingConvention callingConvention)
        {
            var result = RunCecilifier($"using System.Runtime.InteropServices; public class C {{ [DllImport(\"Foo\", CallingConvention = CallingConvention.{callingConvention})] public static extern int M(); int Call() => M(); }}");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode.Contains($"PInvokeAttributes.CallConv{callingConvention.ToString().ToLower()}", StringComparison.OrdinalIgnoreCase), Is.True,cecilifiedCode);
        }

        [Test]
        public void TestEntryPoint()
        {
            var result = RunCecilifier("using System.Runtime.InteropServices; public class C { [DllImport(\"Foo\", EntryPoint=\"NativeMethod\")] public static extern int M(); int Call() => M(); }");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            var match = Regex.Match(cecilifiedCode, "new PInvokeInfo\\(.+, \"NativeMethod\",.+\\);");
            Assert.That(match.Success, Is.True, $"EntryPoint not propagated to PInvokeInfo constructor.{Environment.NewLine}{cecilifiedCode}");
        }

        [Test]
        public void TestCharSet([Values(CharSet.Ansi, CharSet.Auto, CharSet.Unicode)] CharSet charSet)
        {
            var result = RunCecilifier($"using System.Runtime.InteropServices; public class C {{ [DllImport(\"Foo\", CharSet = CharSet.{charSet})] public static extern int M(); }}");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode.Contains($"PInvokeAttributes.CharSet{charSet}"), Is.True,cecilifiedCode);
        }

        [Test]
        public void TestSetLastError([Values] bool setLastError)
        {
            var result = RunCecilifier($"using System.Runtime.InteropServices; public class C {{ [DllImport(\"Foo\", SetLastError = {setLastError.ToString().ToLower()})] public static extern int M(); }}");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode.Contains($"PInvokeAttributes.SupportsLastError"), Is.EqualTo(setLastError), cecilifiedCode);
        }

        [Test]
        public void TestExactSpelling([Values] bool exactSpelling)
        {
            var result = RunCecilifier($"using System.Runtime.InteropServices; public class C {{ [DllImport(\"Foo\", ExactSpelling = {exactSpelling.ToString().ToLower()})] public static extern int M(); }}");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode.Contains($"PInvokeAttributes.NoMangle"), Is.EqualTo(exactSpelling), cecilifiedCode);
        }

        [Test]
        public void TestBestFitMapping([Values] bool bestFitMapping)
        {
            var result = RunCecilifier($"using System.Runtime.InteropServices; public class C {{ [DllImport(\"Foo\", BestFitMapping = {bestFitMapping.ToString().ToLower()})] public static extern int M(); }}");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode.Contains("PInvokeAttributes.BestFitEnabled"), Is.EqualTo(bestFitMapping), cecilifiedCode);
            Assert.That(cecilifiedCode.Contains("PInvokeAttributes.BestFitDisabled"), Is.Not.EqualTo(bestFitMapping), cecilifiedCode);
        }

        [Test]
        public void TestThrowOnUnmappableChar([Values] bool throwOnUnmappableChar)
        {
            var result = RunCecilifier($"using System.Runtime.InteropServices; public class C {{ [DllImport(\"Foo\", ThrowOnUnmappableChar = {throwOnUnmappableChar.ToString().ToLower()})] public static extern int M(); }}");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode.Contains("PInvokeAttributes.ThrowOnUnmappableCharEnabled"), Is.EqualTo(throwOnUnmappableChar), cecilifiedCode);
            Assert.That(cecilifiedCode.Contains("PInvokeAttributes.ThrowOnUnmappableCharDisable"), Is.Not.EqualTo(throwOnUnmappableChar), cecilifiedCode);
        }

        [Test]
        public void Multiple_DllImports_ToSameModule_DoesNotCreate_Multiple_ModuleReferences()
        {
            var result = RunCecilifier("using System.Runtime.InteropServices; public class C { [DllImport(\"Bar\")] public static extern int M(); [DllImport(\"Bar\")] public static extern void M2(); }");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            var matches = Regex.Matches(cecilifiedCode, "new PInvokeInfo\\(.+, \".?\", (?<targetModule>.+)\\);").Distinct();
            var moduleReferenceVariableNames = matches.SelectMany(m => m.Groups["targetModule"].Captures).Select(c => c.Value).Distinct();
            Assert.That(moduleReferenceVariableNames.Count(), Is.EqualTo(1), moduleReferenceVariableNames.Aggregate("Expecting only one ModuleReference instance. Actual:", (acc, curr) => acc + (acc[^1] == ':' ? " " : " ,") + curr));
        }

        [Test]
        public void Multiple_DllImports_ToDifferentModules_DoesNotCreate_Multiple_ModuleReferences()
        {
            var result = RunCecilifier("using System.Runtime.InteropServices; public class C { [DllImport(\"Bar\")] public static extern int M(); [DllImport(\"Bar2\")] public static extern void M2(); }");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            var matches = Regex.Matches(cecilifiedCode, "new PInvokeInfo\\(.+, \".?\", (?<targetModule>.+)\\);");
            Assert.That(matches.Count, Is.EqualTo(2), matches.Aggregate("Actual:", (acc, curr) => acc + (acc[^1] == ':' ? " " : " ,") + curr.Groups["targetModule"].Value));
        }
    }
}
