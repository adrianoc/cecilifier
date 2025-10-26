using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit.ApiDriver;

[TestFixture]
internal partial class DelayedDefinitionsManagerTests
{
    [TestCase("T1V", "T2V")]
    [TestCase("T1V")]
    public void NoMethods(params string[] typeVariables)
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        foreach (var typeVariable in typeVariables)
        {
            context.DelayedDefinitionsManager.RegisterTypeDefinition(typeVariable, "NOT_IMPORTANT", testContext.OnTypeRegistration);
        }

        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        foreach (var typeVariable in typeVariables)
            Assert.That(testContext.Result[typeVariable].FirstMethodHandle, Is.EqualTo("MetadataTokens.MethodDefinitionHandle(1)"));
    }
    
    [Test]
    public void SingleTypeWithSingleMethod()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T1V", (ctx, tdr) => "T1M1");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstMethodHandle, Is.EqualTo("T1M1"));
    }
    
    [Test]
    public void SingleTypeWithTwoMethods()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        
        var context = NewContext();
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T1V1", (ctx, tdr) => "T1M1");
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T1V2", (ctx, tdr) => "T1M2");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstMethodHandle, Is.EqualTo("T1M1"));
    }
    
    [TestCase("T1V")]
    [TestCase("T2V")]
    public void TwoTypes_MethodOnlyInOne(string declaringTypeName)
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterMethodDefinition(declaringTypeName, (ctx, tdr) => "TheMethod");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T2V", "T2", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstMethodHandle, Is.EqualTo("TheMethod"));
        Assert.That(testContext.Result["T2V"].FirstMethodHandle, Is.EqualTo("TheMethod"));
    }        
    
    [Test]
    public void TwoTypes_MethodsInBoth()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T1V", (ctx, tdr) => "T1M");
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T2V", (ctx, tdr) => "T2M");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T2V", "T2", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstMethodHandle, Is.EqualTo("T1M"));
        Assert.That(testContext.Result["T2V"].FirstMethodHandle, Is.EqualTo("T2M"));
    }    
    
    [Test]
    public void TreeTypes_MethodsInLastTwo()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T2V", (ctx, tdr) => "T2M");
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T3V", (ctx, tdr) => "T3M");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T2V", "T2", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T3V", "T3", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstMethodHandle, Is.EqualTo("T2M"));
        Assert.That(testContext.Result["T2V"].FirstMethodHandle, Is.EqualTo("T2M"));
        Assert.That(testContext.Result["T3V"].FirstMethodHandle, Is.EqualTo("T3M"));
    }
    
    [Test]
    public void FourTypes_MethodsInSecondAndLastOnes()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T2V", (ctx, tdr) => "T2M");
        context.DelayedDefinitionsManager.RegisterMethodDefinition("T4V", (ctx, tdr) => "T4M");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T2V", "T2", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T3V", "T3", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T4V", "T4", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstMethodHandle, Is.EqualTo("T2M"));
        Assert.That(testContext.Result["T2V"].FirstMethodHandle, Is.EqualTo("T2M"));
        Assert.That(testContext.Result["T3V"].FirstMethodHandle, Is.EqualTo("T4M"));
        Assert.That(testContext.Result["T4V"].FirstMethodHandle, Is.EqualTo("T4M"));
    }
    
    protected override string Snippet => "class NotRelevant {}"; 
}
