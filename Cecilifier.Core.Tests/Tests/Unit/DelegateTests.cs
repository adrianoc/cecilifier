using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class DelegateTests : CecilifierUnitTestBase
{
    [Test]
    public void InnerDelegateDeclaration()
    {
        var result = RunCecilifier("class C { public delegate int D(); }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match(@"var (del_D\d+) = new TypeDefinition\("""", ""D"", .+ImportReference\(typeof\(System.MulticastDelegate\)\)\).+;"));
        Assert.That(cecilifiedCode, Does.Match(@"var m_invoke_\d+ = new MethodDefinition\(""Invoke"", .+assembly.MainModule.TypeSystem.Int32\)"));

        Assert.That(cecilifiedCode, Does.Match(@"var m_beginInvoke_\d+ = new MethodDefinition\(""BeginInvoke"",.+ImportReference\(typeof\(System\.IAsyncResult\)\)\).*"));
        Assert.That(cecilifiedCode, Does.Match(@"m_beginInvoke_\d+.Parameters.Add\(new ParameterDefinition\(assembly.MainModule.ImportReference\(typeof\(System.AsyncCallback\)\)\)\);\s+"));
        Assert.That(cecilifiedCode, Does.Match(@"m_beginInvoke_\d+.Parameters.Add\(new ParameterDefinition\(assembly.MainModule.TypeSystem.Object\)\);\s+"));
        
        Assert.That(cecilifiedCode, Does.Match(@"var m_endInvoke_\d+ = new MethodDefinition\(""EndInvoke"",.+assembly.MainModule.TypeSystem.Int32\);\s+"));
        Assert.That(cecilifiedCode, Does.Match(@"var p_ar_\d+ = new ParameterDefinition\(""ar"",.+ImportReference\(typeof\(System.IAsyncResult\)\)\);"));
        
        Assert.That(cecilifiedCode, Does.Match(@"cls_C_\d+.NestedTypes.Add\(del_D\d+\);"), "Inner delegate should be added as a nested type of C");
    }
    
    [Test]
    public void InnerDelegate_AsParameter()
    {
        var result = RunCecilifier("class C { public delegate int D(); D M(D p) => p; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match(@"var m_M_\d+ = new MethodDefinition\(""M"",.+, del_D1\);"), "Return type");
        Assert.That(cecilifiedCode, Does.Match(@"var p_p_\d+ = new ParameterDefinition\(""p"",.+del_D1\);"), "Parameter");
    }
}
