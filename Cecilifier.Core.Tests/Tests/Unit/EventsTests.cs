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
        Assert.That(cecilified, Does.Match(@"m_add_\d+.Parameters.Add\(.*Action.*\);"));
        Assert.That(cecilified, Does.Match(@"var il_add_\d+ = m_add_\d+.Body.GetILProcessor\(\);"));
        
        Assert.That(cecilified, Does.Match(@"var m_remove_\d+ = new MethodDefinition\(""remove_E"", .+, assembly.MainModule.TypeSystem.Void\);"));
        Assert.That(cecilified, Does.Match(@"m_remove_\d+.Parameters.Add\(.*Action.*\);"));
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
}
