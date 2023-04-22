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

    [Test]
    public void TestCallingOverloadedMethod()
    {
        var result = RunCecilifier(
            """
                class Base { public virtual string FromBase() => "Foo"; }
                class Derived : Base 
                { 
                    public override string FromBase() => "Bar";
                    public string CallBase() => GetType().FullName; 
                    public string CallOverloadedInType() => FromBase(); 
                }
                """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, 
            Does.Match(
            """
            \s+//Method : CallOverloadedInType
            \s+var (m_callOverloadedInType_\d+) = new MethodDefinition\("CallOverloadedInType", MethodAttributes.Public \| MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.String\);
            \s+cls_derived_5.Methods.Add\(\1\);
            \s+\1\.Body\.InitLocals = true;
            \s+var (il_callOverloadedInType_\d+) = \1\.Body\.GetILProcessor\(\);
            \s+(\2\.Emit\(OpCodes\.)Ldarg_0\);
            \s+\3Callvirt, m_fromBase_\d+\);
            \s+\3Ret\);
            """));
        
        Assert.That(
            cecilifiedCode, 
            Does.Match(
            """
            \s+//Method : CallBase
            \s+var m_callBase_\d+ = new MethodDefinition\("CallBase", MethodAttributes.Public \| MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.String\);
            \s+cls_derived_5.Methods.Add\(m_callBase_\d+\);
            \s+m_callBase_\d+.Body.InitLocals = true;
            \s+var (il_callBase_\d+) = m_callBase_8.Body.GetILProcessor\(\);
            \s+(\1\.Emit\(OpCodes\.)Ldarg_0\);
            \s+\2Call, .+"GetType".+\);
            \s+\2Callvirt,.+"get_FullName".+\);
            \s+\2Ret\);
            """));
    }
}
