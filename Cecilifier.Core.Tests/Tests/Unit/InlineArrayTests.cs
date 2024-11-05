using Cecilifier.Core.Tests.Tests.Unit.Framework;
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
        Assert.That(cecilifiedCode, Does.Match("""cls_privateImplementationDetails_\d+.Methods.Add\(m_inlineArrayAsSpan_\d+\);"""));
        Assert.That(cecilifiedCode, Does.Match("""l_inlineArrayAsSpan_\d+.Add\(il_inlineArrayAsSpan_\d+.Create\(OpCodes.Call, gi_unsafeAs_\d+\)\);"""));
        Assert.That(cecilifiedCode, Does.Match("""l_inlineArrayAsSpan_\d+.Add\(il_inlineArrayAsSpan_\d+.Create\(OpCodes.Call, gi_createSpan_\d+\)\);"""));
    }

    [Test]
    public void AssignmentToFirstElement_MapsTo_PrivateImplementationDetailsInlineArrayFirstElementRefMethod()
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
    
    [Test]
    public void AccessToFirstElement_MapsTo_PrivateImplementationDetailsInlineArrayFirstElementRefMethod()
    {
        var result = RunCecilifier("""
                                   var buffer = new IntBuffer();
                                   buffer[0] = 42;
                                   System.Console.WriteLine(buffer[0]);
                                   
                                   [System.Runtime.CompilerServices.InlineArray(10)]
                                   public struct IntBuffer
                                   {
                                       private int _element0;
                                   }
                                   """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilified, Does.Match("""
                                          (il_topLevelMain_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                          \s+\1Ldind_I4\);
                                          \s+\1Call,.+ImportReference\(.+ResolveMethod\(typeof\(System.Console\), "WriteLine".+\);
                                          """));
    }
    
    [TestCase("field[0] = 42", @"Ldflda, fld_field_\d+", TestName = "Field")]
    [TestCase("parameter[0] = 42", @"Ldarga, p_parameter_\d+", TestName = "Parameter")]
    public void AccessToFirstElement_Of_StorageLocation(string statement, string expectedOpCode)
    {
        var result = RunCecilifier($$"""
                                   class C
                                   {
                                       public IntBuffer field;
                                       void M(IntBuffer parameter) { {{statement}}; }
                                   }
                                   
                                   [System.Runtime.CompilerServices.InlineArray(10)]
                                   public struct IntBuffer { private int _element0; }
                                   """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilified, Does.Match($"""
                                          (il_M_\d+\.Emit\(OpCodes\.){expectedOpCode}\);
                                          \s+//\<PrivateImplementationDetails\> class.
                                          """));
        
        Assert.That(cecilified, Does.Match("""
                                          (il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                          \s+\1Ldc_I4, 42\);
                                          \s+\1Stind_I4\);
                                          """));
    }
    
    [TestCase("field[42] = 1", @"Ldflda, fld_field_\d+", TestName = "Field")]
    [TestCase("parameter[42] = 1", @"Ldarga, p_parameter_\d+", TestName = "Parameter")]
    [TestCase("local[42] = 1", @"Ldloca, l_local_\d+", TestName = "Local")]
    public void AccessToNonFirstElement_Of_StorageLocation(string statement, string expectedOpCode)
    {
        var result = RunCecilifier($$"""
                                   class C
                                   {
                                       public IntBuffer field;
                                       void M(IntBuffer parameter) 
                                       { 
                                          var local = new IntBuffer();
                                          {{statement}}; 
                                       }
                                   }
                                   
                                   [System.Runtime.CompilerServices.InlineArray(100)]
                                   public struct IntBuffer { private int _element0; }
                                   """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilified, Does.Match($"""
                                          (\s+il_M_\d+\.Emit\(OpCodes\.){expectedOpCode}\);
                                          \1Ldc_I4, 42\);
                                          \s+//\<PrivateImplementationDetails\> class.
                                          """));
        
        Assert.That(cecilified, Does.Match("""
                                          (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayElementRef_\d+\);
                                          \s+\1Ldc_I4, 1\);
                                          \s+\1Stind_I4\);
                                          """));
    }
    
    [TestCase("string", "\"Foo\"", """
                                (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                \1Ldstr, "Foo"\);
                                \1Stind_Ref\);
                                """, TestName = "String constant")]
    
    [TestCase("object", "null", """
                                (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                \1Ldnull\);
                                \1Stind_Ref\);
                                """, TestName = "Object (null)")]
    
    [TestCase("CustomStruct", "new CustomStruct(42)", """
                                (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                \1Ldc_I4, 42\);
                                \1Newobj, ctor_customStruct_\d+\);
                                \1Stobj\);
                                """, TestName = "Custom struct (new expression)")]
    
    [TestCase("CustomStruct", "new CustomStruct { name = \"Foo\" }", """
                                (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                \s+var (l_vt_\d+) = new VariableDefinition\((?<struct_type>st_customStruct_\d+)\);
                                \s+m_M_\d+\.Body\.Variables.Add\(\2\);
                                (?<lsa>\1Ldloca_S, \2\);)
                                \1Initobj, \k<struct_type>\);
                                \k<lsa>
                                \1Dup\);
                                \1Ldstr, "Foo"\);
                                \1Stfld, fld_name_\d+\);
                                \1Pop\);
                                \1Ldloc, \2\);
                                \1Stobj, \k<struct_type>\);
                                """, TestName = "Custom struct (object initializer)")]
    public void InlineArray_ElementType(string elementType, string value, string expectedIL)
    {
        var result = RunCecilifier($$"""
                                   class C
                                   {
                                       void M(Buffer b, {{elementType}} value) 
                                       {
                                           b[0] = {{value}};
                                           b[1] = value;
                                       }
                                   }
                                   
                                   struct CustomStruct
                                   {
                                       public CustomStruct(int value) {}
                                       public string name;
                                   }
                                   
                                   [System.Runtime.CompilerServices.InlineArray(5)]
                                   public struct Buffer { private {{elementType}} _element0; }
                                   """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilified, Does.Match(expectedIL));
    }

    [Test]
    public void AccessToIndex_ThroughNonLiteral_UsesElementRefMethodAndIndex()
    {
        var result = RunCecilifier("""
                                     class C
                                     {
                                         int M(int i)
                                         {
                                            Buffer b = new Buffer();
                                            return b[i];
                                         }
                                     }

                                     [System.Runtime.CompilerServices.InlineArray(5)]
                                     public struct Buffer { private int _element0; }
                                     """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilified, Does.Match("""
                                           \s+//return b\[i\];
                                           (\s+il_M_5.Emit\(OpCodes\.)Ldloca, l_b_7\);
                                           \1Ldarg_1\);
                                           """));      
        
        Assert.That(cecilified, Does.Match("""
                                           (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayElementRef_\d+\);
                                           \1Ldind_I4\);
                                           """));
    }
    
    [Test]
    public void InlineArray_MemberAccess_OnIndex()
    {
        var result = RunCecilifier($$"""
                                   class C
                                   {
                                       int M(Buffer b) => b[0].Value; 
                                   }
                                   
                                   struct CustomStruct
                                   {
                                      public int Value;
                                   }
                                   
                                   [System.Runtime.CompilerServices.InlineArray(5)]
                                   public struct Buffer { private CustomStruct _element0; }
                                   """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilified, Does.Match("""
                                           (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
                                           \1Ldfld, fld_value_\d+\);
                                           """));
    }

    [TestCase("T M<T>(Buffer<T> b) => b[0];",
        """
        (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
        \1Ldobj, gp_T_\d+\);
        """, TestName = "Open generic method")]
    
    [TestCase("int M(Buffer<int> bi) => bi[0];", 
        """
        (\s+il_M_\d+.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
        \1Ldind_I4\);
        """, TestName = "Closed generic type (primitive type)")]
    
    [TestCase("CustomStruct M(Buffer<CustomStruct> b) => b[0];", 
        """
        (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
        \1Ldobj, st_customStruct_0\);
        """, TestName = "Closed generic type (custom struct type)")]
    
    [TestCase("TC M(Buffer<TC> b) => b[0];", 
        """
        (\s+il_M_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayFirstElementRef_\d+\);
        \1Ldobj, gp_tC_\d+\);
        """, TestName = "Type Parameter from declaring type")]
    
    [TestCase("T M<T>(Buffer<T> b) where T : Itf => b[0];",
        """
        (gi_inlineArrayFirstElementRef_\d+).GenericArguments.Add\((gp_T_\d+)\);
        (\s+il_M_\d+\.Emit\(OpCodes\.)Call, \1\);
        \3Ldobj, \2\);
        """, TestName = "Interface as Type Parameter")]
    
    [TestCase("int M<T>(Buffer<T> b) where T : Itf => b[0].Value;",
        """
        (gi_inlineArrayFirstElementRef_\d+).GenericArguments.Add\((gp_T_\d+)\);
        (\s+il_M_\d+\.Emit\(OpCodes\.)Call, \1\);
        \3Constrained, \2\);
        \3Callvirt, m_get_\d+\);
        """, TestName = "Member access on interface")]
    
    [TestCase("int M<T>(Buffer<T> b) where T : CustomClass => b[0].Value;",
        """
        (gi_inlineArrayFirstElementRef_\d+).GenericArguments.Add\((gp_T_\d+)\);
        (\s+il_M_\d+\.Emit\(OpCodes\.)Call, \1\);
        \3Ldobj, \2\);
        \3Box, \2\);
        \3Ldfld, fld_value_\d+\);
        """, TestName = "Field access on class")]
    public void InlineArray_ElementAccess_OnGenericType(string toBeTested, string expecetdIL)
    {
        var result = RunCecilifier($$"""
                                     struct CustomStruct { public int Value; }
                                     class CustomClass { public int Value; }
                                     
                                     public interface Itf { int Value { get; set; } }
                                     
                                     [System.Runtime.CompilerServices.InlineArray(5)]
                                     public struct Buffer<T> { private T _element0; }
                                     
                                     class C<TC> { {{toBeTested}} }
                                     """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilified, Does.Match(expecetdIL));
    }

    [Test]
    public void Slice_Range()
    {
        var result = RunCecilifier("""
                                     class C
                                     {
                                         System.Span<int> M(ref Buffer b) => b[1..3];
                                     }

                                     [System.Runtime.CompilerServices.InlineArray(5)]
                                     public struct Buffer { private int _element0; }
                                     """);

        var cecilified = result.GeneratedCode.ReadToEnd();
        Assert.Ignore($"Just for testing. Output:\n{cecilified}");
    }
}
