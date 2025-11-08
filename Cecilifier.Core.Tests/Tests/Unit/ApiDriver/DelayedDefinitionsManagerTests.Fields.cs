using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit.ApiDriver;

[TestFixture]
internal partial class DelayedDefinitionsManagerTests : CecilifierContextBasedTestBase<SystemReflectionMetadataContext>
{
    [Test]
    public void SingleType_NoFields()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstFieldHandle, Is.EqualTo("MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1)"));
    }
    
    [Test]
    public void SingleType_SingleField()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterFieldDefinition("T1V", "T1F");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstFieldHandle, Is.EqualTo("T1F"));
    }
    
    [Test]
    public void SingleType_MultipleFields()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterFieldDefinition("T1V", "T1F1");
        context.DelayedDefinitionsManager.RegisterFieldDefinition("T1V", "T1F2");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstFieldHandle, Is.EqualTo("T1F1"));
    }
    
    [Test]
    public void TwoTypes_FieldOnlyInSecondType()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterFieldDefinition("T2V", "T2F");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T2V", "T2", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstFieldHandle, Is.EqualTo("MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1)"));
        Assert.That(testContext.Result["T2V"].FirstFieldHandle, Is.EqualTo("T2F"));
    }
    
    [TestCase("T1V")]
    [TestCase("T2V")]
    public void TwoTypes_FieldOnlyInOne(string declaringTypeVariable)
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterFieldDefinition(declaringTypeVariable, "TheField");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T2V", "T2", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstFieldHandle, Is.EqualTo(declaringTypeVariable == "T1V" ? "TheField" : "MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1)"));
        Assert.That(testContext.Result["T2V"].FirstFieldHandle, Is.EqualTo(declaringTypeVariable == "T2V" ? "TheField" : "MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1)"));
    }
    
    [Test]
    public void TreeTypes_FieldsInLastTwo()
    {
        var testContext = new DelayedDefinitionsManagerTestContext();
        var context = NewContext();
        
        context.DelayedDefinitionsManager.RegisterFieldDefinition("T2V", "T2F");
        context.DelayedDefinitionsManager.RegisterFieldDefinition("T3V", "T3F");
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T1V", "T1", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T2V", "T2", testContext.OnTypeRegistration);
        context.DelayedDefinitionsManager.RegisterTypeDefinition("T3V", "T3", testContext.OnTypeRegistration);
        
        context.DelayedDefinitionsManager.ProcessDefinitions(context);
        
        Assert.That(testContext.Result["T1V"].FirstFieldHandle, Is.EqualTo("MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1)"));
        Assert.That(testContext.Result["T2V"].FirstFieldHandle, Is.EqualTo("T2F"));
        Assert.That(testContext.Result["T3V"].FirstFieldHandle, Is.EqualTo("T3F"));
    }    
}
