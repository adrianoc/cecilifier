using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class InterfaceTests : CecilifierUnitTestBase
{
    [TestCase("public interface IFoo<T> { abstract static T M(); }", "Void", TestName = "With Generic")]
    [TestCase("public interface IFoo { abstract static int M(); }", "Int32", TestName = "Simple")]
    public void AbstractStaticMethodDefinitionTest(string code, string expectedReturnTypeInDeclaration)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            $"""
            new MethodDefinition("M", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Abstract, assembly.MainModule.TypeSystem.{expectedReturnTypeInDeclaration});
            """));
    }
}
