using System;
using System.Collections.Generic;
using System.IO;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class FormattingOptionsTests
    {
        private Dictionary<ElementKind, string> prefixes;

        [SetUp]
        public void SetupFixture()
        {
            prefixes = new Dictionary<ElementKind, string>();
            foreach (var elementKind in Enum.GetValues<ElementKind>())
            {
                prefixes[elementKind] = elementKind.ToString();
            }
        }

        [TestCase(ElementKind.Attribute, "changed_obsolete", "Attribute_obsolete")]
        [TestCase(ElementKind.Class, "changed_foo", "Class_foo")]
        [TestCase(ElementKind.Constructor, "changed_foo", "Constructor_foo")]
        [TestCase(ElementKind.Delegate, "changed_del", "Delegate_del")]
        [TestCase(ElementKind.Enum, "changed_E", "Enum_E")]
        [TestCase(ElementKind.Event, "changed_evt", "Event_evt")]
        [TestCase(ElementKind.Field, "changed_field", "Field_field")]
        [TestCase(ElementKind.Interface, "changed_I", "Interface_I")]
        [TestCase(ElementKind.Label, "changed_", "Label")]
        [TestCase(ElementKind.Method, "changed_bar", "Method_bar")]
        [TestCase(ElementKind.Parameter, "changed_param1", "Parameter_param1")]
        [TestCase(ElementKind.Property, "changed_prop1", "Property_prop1")]
        [TestCase(ElementKind.Struct, "changed_S", "Struct_S")]
        [TestCase(ElementKind.Record, "changed_R", "Record_S")]
        [TestCase(ElementKind.StaticConstructor, "changed_foo", "StaticConstructor_foo")]
        [TestCase(ElementKind.GenericInstance, "changed_gen", "GenericInstance_gen")]
        [TestCase(ElementKind.GenericParameter, "changed_gP", "GenericParameter_gP")]
        [TestCase(ElementKind.IL, "changed_bar", "IL_bar")]
        [TestCase(ElementKind.LocalVariable, "changed_local1", "LocalVariable_local1")]
        public void ElementKind_Is_Used(ElementKind elementKind, string expected, string notExpected)
        {
            const string source = "using System; public record R; public delegate void Del(); class Foo { static Foo() {} public Foo() {} void Gen<GP>() {} [Obsolete] int Obsolete; event Action Evt; public void Bar(int param1) { int local1 = 0; if (param1 == local1) {} Gen<int>();} int field; int Prop1 {get;set;} } struct S {} enum E {} interface I {}";
            prefixes[elementKind] = "changed";

            var nameStrategy = new DefaultNameStrategy(NamingOptions.All, prefixes);
            var memoryStream = new MemoryStream();
            memoryStream.Write(System.Text.Encoding.ASCII.GetBytes(source));
            memoryStream.Position = 0;

            var cecilified = Cecilifier.Process(memoryStream, new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath(), Naming = nameStrategy }).GeneratedCode.ReadToEnd();

            Assert.That(cecilified, Does.Match($"\\b{expected}"), $"{elementKind} prefix not applied.");
            Assert.That(cecilified, Does.Not.Match($"\\b{notExpected}"), $"{elementKind} prefix not applied.");
        }

        [Test, Combinatorial]
        public void Casing_Setting_Is_Respected([Values] ElementKind elementKind, [Values] bool camelCasing)
        {
            if (elementKind == ElementKind.None)
                return;

            if (elementKind == ElementKind.Label && !camelCasing)
            {
                Assert.Ignore("as of Aug/2021 all created labels are camelCase.");
                return;
            }

            const string source = "using System; public delegate void TestDelegate(); class TestClass { static TestClass() {} public TestClass() {} void Gen<TestGenericParameter>() {} [Obsolete] int TestAttribute; event Action TestEvent; public void Bar(int TestParameter) { int local1 = 0; if (TestParameter == local1) {} Gen<int>(); } int TestField; int TestProperty {get;set;} } struct TestStruct {} enum TestEnum {} interface TestInterface {}";
            var namingOptions = NamingOptions.All;
            var casingValidation = "a-z";

            if (!camelCasing)
            {
                namingOptions &= ~NamingOptions.CamelCaseElementNames;
                casingValidation = "A-Z";
            }

            var nameStrategy = new DefaultNameStrategy(namingOptions, prefixes);
            var memoryStream = new MemoryStream();
            memoryStream.Write(System.Text.Encoding.ASCII.GetBytes(source));
            memoryStream.Position = 0;

            var cecilified = Cecilifier.Process(memoryStream, new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath(), Naming = nameStrategy }).GeneratedCode.ReadToEnd();

            Assert.That(cecilified, Does.Match($"{elementKind}_[{casingValidation}][a-zA-Z]+"), "Casing");
        }
    }
}
