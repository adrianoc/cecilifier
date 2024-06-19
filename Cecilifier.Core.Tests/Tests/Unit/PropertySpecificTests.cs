using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class PropertySpecificTests : CecilifierUnitTestBase
{
    [Test]
    public void AccessorAccessibility_IsRespected()
    {
        var code = """
                   class Foo
                   {
                       public int Value
                       {
                           private get => 1;
                           set {  }
                       }
                   }
                   """;

        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               //Getter
                                               \s+var m_get_\d+ = new MethodDefinition\("get_Value", MethodAttributes.Private.+, .+TypeSystem.Int32\);
                                               """));
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               // Setter
                                               \s+var l_set_\d+ = new MethodDefinition\("set_Value", MethodAttributes.Public.+, .+TypeSystem.Void\);
                                               """));
    }
}
