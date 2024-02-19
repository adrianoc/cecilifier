using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class InlineArrayTests : CecilifierUnitTestBase
{
    [Test]
    public void Instantiating_InlineArray_EmitsInitObj()
    {
        var result = RunCecilifier("""
                                   var b = new IntBuffer();
                                   
                                   [System.Runtime.CompilerServices.InlineArray(10)]
                                   public struct IntBuffer
                                   {
                                       private int _element0;
                                   }
                                   """);
        
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(
            @"m_topLevelStatements_\d+.Body.Variables.Add\((?<ia_var>l_b_\d+)\);\s+"+  // local variable *b*
            @"(?<emit>il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \k<ia_var>\);\s+" +      // Loads *b* address 
            @"\k<emit>Initobj, st_intBuffer_\d+\);"));                                       // Execute *initobj* on *b*
    }
    
    [TestCase("System.Span<int> span = l;", TestName = "Local variable initialization")]
    [TestCase("scoped System.Span<int> span; span = l;", TestName = "Local Variable assignment")]
    [TestCase("Consume(l);", TestName = "Local passed as argument")]
    [TestCase("Consume(p);", TestName = "Parameter passed as argument")]
    public void Assigning_InlineArrayToSpan_EmitsPrivateImplementationDetailsType(string triggeringStatements)
    {
        var result = RunCecilifier($$"""
                                   void TestMethod(IntBuffer p)
                                   {
                                        var l = new IntBuffer();
                                   
                                       // This will trigger the emission of <PrivateImplementationDetails>.InlineArrayAsSpan() method
                                       {{triggeringStatements}}
                                   }
                                   
                                   void Consume(System.Span<int> span) {}
                                   
                                   [System.Runtime.CompilerServices.InlineArray(10)]
                                   public struct IntBuffer
                                   {
                                       private int _element0;
                                   }
                                   """);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""new TypeDefinition\("", "<PrivateImplementationDetails>", .+\)"""));
    }

    [Test]
    public void AccessToFirstElement_MapsTo_PrivateImplementationDetailsInlineArrayFirstElementRefMethod()
    {
        var result = RunCecilifier("""
                                   var buffer = new IntBuffer();
                                   buffer[0] = 42;
                                   
                                   [System.Runtime.CompilerServices.InlineArray(10)]
                                   public struct IntBuffer
                                   {
                                       private int _element0;
                                   }
                                   """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        
        // assert that the inline array address is being pushed to the stack...
        Assert.That(cecilified, Does.Match("""
                                          il_topLevelMain_\d+\.Emit\(OpCodes\.Ldloca, l_buffer_\d+\);

                                          \s+//<PrivateImplementationDetails> class.
                                          """));
        
        // and later <PrivateImplementationDetails>.InlineArrayFirstElementRef() static method is being invoked
        // and the value 42 stored in the address at the top of the stack.
        Assert.That(cecilified, Does.Match("""
                                          (il_topLevelMain_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                          \s+\1Ldc_I4, 42\);
                                          \s+\1Stind_I4
                                          """));
    }
    
    // Access to not first element
    // 
}
