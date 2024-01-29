using System.Collections.Generic;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class MiscellaneousAssignmentsTests : CecilifierUnitTestBase
    {
        [TestCaseSource(nameof(TestScenarios))]
        public void TestIndirectLoad_Issue259(string typeName, string expectedLoadOpcode, bool initializeVariable)
        {
            var code = $$"""
                       struct S {}
                       class Foo { {{typeName}} Bar(ref {{typeName}} p) {  {{typeName}} local{{ (initializeVariable ? "" : "; local") }} = p; return local; } }
                       """;

            var expectedSnippet = @".*(?<emit>.+\.Emit\(OpCodes\.)Ldarg_1\);\s"
                                  + $@"\1{expectedLoadOpcode}.+;\s"
                                  + @"\1Stloc, l_local_\d+\);\s";

            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }

        static IEnumerable<TestCaseData> TestScenarios()
        {
            foreach(var initializeMode in  new[] { true, false } )
            {
                yield return new  TestCaseData("int", "Ldind_I4", initializeMode);
                yield return new  TestCaseData("long", "Ldind_I8", initializeMode);
                yield return new  TestCaseData("short", "Ldind_I2", initializeMode);
                yield return new  TestCaseData("byte", "Ldind_U1", initializeMode);
                yield return new  TestCaseData("float", "Ldind_R4", initializeMode);
                yield return new  TestCaseData("double", "Ldind_R8", initializeMode);
                yield return new  TestCaseData("bool", "Ldind_U1", initializeMode);
                yield return new  TestCaseData("char", "Ldind_U2", initializeMode);
                yield return new  TestCaseData("int[]", "Ldind_Ref", initializeMode);
                yield return new  TestCaseData("System.DateTime", "Ldobj", initializeMode);
                yield return new  TestCaseData("S", "Ldobj", initializeMode);
            }
        }

        public record struct TestScenario(string TypeName, string ExpectedLoadOpcode, bool VariableInitializer);
    }
}
