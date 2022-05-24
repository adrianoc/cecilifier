using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class AttributesTest : CecilifierUnitTestBase
{
    [Test]
    public void TestAttributeAppliedToItsOwnType()
    {
        var result = RunCecilifier("[My(\"type\")] class MyAttribute : System.Attribute { public MyAttribute(string message) {} }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(@"var ctor_myAttribute_\d+ = new MethodDefinition\("".ctor"", MethodAttributes.Private, assembly.MainModule.TypeSystem.Void\);")); // This represents the ctor with argument
        Assert.That(cecilifiedCode, Does.Not.Match(@"var ctor_myAttribute_\d+ = new MethodDefinition\("".ctor"", MethodAttributes.Public \| MethodAttributes.HideBySig \| MethodAttributes.RTSpecialName \| MethodAttributes.SpecialName, assembly.MainModule.TypeSystem.Void\);"), "Parameterless ctor not expected");
        Assert.That(cecilifiedCode, Does.Match(@"cls_myAttribute_\d+\.CustomAttributes.Add\(attr_my_\d+\);"), "Custom attribute should be be added");
        Assert.That(cecilifiedCode, Does.Match(@"var attr_my_\d+ = new CustomAttribute\(ctor_myAttribute_\d+\);"), "Reference to class declaring MyAttribute should be used when instantiating the custom attribute");
    }
    
    [Test]
    public void TestAttributeAppliedToAssembly()
    {
        var result = RunCecilifier("[assembly:My(\"assembly\")] class MyAttribute : System.Attribute { public MyAttribute(string message) {} }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match(@"var cls_myAttribute_0 = new TypeDefinition\("""", ""MyAttribute"", TypeAttributes.AnsiClass \| TypeAttributes.BeforeFieldInit \| TypeAttributes.NotPublic, .+\);"), "Expected TypeDefinition for attribute not found.");
        Assert.That(cecilifiedCode, Does.Match(@"var attr_my_1 = new CustomAttribute\(ctor_myAttribute_2\);"), "Reference to class declaring MyAttribute should be used when instantiating the custom attribute");
        Assert.That(cecilifiedCode, Does.Match(@"assembly.CustomAttributes.Add\(attr_my_1\);"), "Custom attribute should be be added to assembly");
    }
}
