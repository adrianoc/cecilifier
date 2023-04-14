using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class MethodTests : CecilifierUnitTestBase
{
    [Test]
    public void Covariant()
    {
        var result = RunCecilifier("class B { public virtual B Get() => null; } class D : B { public override D Get() => new D(); D CallIt() => Get(); }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Contains.Substring("var m_get_6 = new MethodDefinition(\"Get\", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot, cls_D_5);"));
        Assert.That(cecilifiedCode, Does.Match(@"m_get_6\.CustomAttributes\.Add\(.+typeof\(.+PreserveBaseOverridesAttribute\).+\);"));
        Assert.That(cecilifiedCode, Contains.Substring("il_callIt_10.Emit(OpCodes.Callvirt, m_get_6);"));
    }

    [Test]
    public void InterfaceImplementation()
    {
        var result = RunCecilifier("using System.Collections; class B : IEnumerable { public IEnumerator GetEnumerator() => null; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring("\"GetEnumerator\", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final"));
    }
}
