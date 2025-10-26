using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class EventsTests : CecilifierUnitTestBase
{
    [Test]
    public void EventDeclaration()
    {
        var code = @"using System; 
                    class C 
                    { 
                        event Action E	
                        { 
                            add { Console.WriteLine(""add""); } 
                            remove { Console.WriteLine(""remove""); } 
                        }
                    }";

        var result = RunCecilifier(code);

        var cecilified = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilified, Does.Match(@"cls_C_0.Events.Add\(evt_E_\d+\);"));
        Assert.That(cecilified, Does.Match(@"var m_add_\d+ = new MethodDefinition\(""add_E"", .+, assembly.MainModule.TypeSystem.Void\);"));
        Assert.That(cecilified, Does.Match("""
                                           \s+var (p_value_\d+) = new ParameterDefinition\("value", .+System.Action.+\);
                                           \s+m_add_\d+.Parameters.Add\(\1\);
                                           """));
        Assert.That(cecilified, Does.Match(@"var il_add_\d+ = m_add_\d+.Body.GetILProcessor\(\);"));

        Assert.That(cecilified, Does.Match(@"var m_remove_\d+ = new MethodDefinition\(""remove_E"", .+, assembly.MainModule.TypeSystem.Void\);"));
        Assert.That(cecilified, Does.Match("""
                                           \s+var (p_value_\d+) = new ParameterDefinition\("value", .+System.Action.+\);
                                           \s+m_remove_\d+.Parameters.Add\(\1\);
                                           """));
        Assert.That(cecilified, Does.Match(@"var il_remove_\d+ = m_remove_\d+.Body.GetILProcessor\(\);"));

        Assert.That(cecilified, Does.Match(@"cls_C_\d+.Methods.Add\(m_add_\d+\);"));
        Assert.That(cecilified, Does.Match(@"cls_C_\d+.Methods.Add\(m_remove_\d+\);"));
    }

    [Test]
    public void EventSubscription()
    {
        var code = @"using System; 
                    class C 
                    { 
                        event Action E { add { Console.WriteLine(""add""); } remove { Console.WriteLine(""remove""); } }
                        void Sub(Action a) 
                        { 
                            E += a; 
                            E -= a; 
                        }
                    }";

        var result = RunCecilifier(code);

        var cecilified = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilified, Does.Match(@"Call, m_add_\d+"));
        Assert.That(cecilified, Does.Match(@"Call, m_remove_\d+"));
    }

    [Test]
    public void ForwardMethodReferenceTest()
    {
        var code = @"using System; 
                    class C 
                    { 
                        event Action E;
                        void Sub() { E += M;  }

                        void M() {}
                    }";

        var result = RunCecilifier(code);

        var cecilified = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilified, Does.Match(@"Call, m_add_\d+"));
    }

    [TestCase("add { int l = 1; } remove { }", @"m_add_\d+", TestName = "Add")]
    [TestCase("add { } remove { int l = 1; }", @"m_remove_\d+", TestName = "Remove")]
    public void TestEventAccessorWithLocalVariables(string accessorDeclaration, string targetMethod)
    {
        var result = RunCecilifier($"using System; class C {{ event Action E {{ {accessorDeclaration} }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(
            @"var (l_l_\d+) = new VariableDefinition\(assembly\.MainModule\.TypeSystem\.Int32\);\s+" +
            $@"{targetMethod}\.Body\.Variables\.Add\(\1\);"));
    }
}
