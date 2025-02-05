using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class AttributesTest : CecilifierUnitTestBase
{
    [Test]
    public void TestAttributeAppliedToItsOwnType()
    {
        var result = RunCecilifier($"[My(\"type\")] {AttributeDefinition}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(@"var ctor_myAttribute_\d+ = new MethodDefinition\("".ctor"", MethodAttributes.Private, assembly.MainModule.TypeSystem.Void\);")); // This represents the ctor with argument
        Assert.That(cecilifiedCode, Does.Not.Match(@"var ctor_myAttribute_\d+ = new MethodDefinition\("".ctor"", MethodAttributes.Public \| MethodAttributes.HideBySig \| MethodAttributes.RTSpecialName \| MethodAttributes.SpecialName, assembly.MainModule.TypeSystem.Void\);"), "Parameterless ctor not expected");
        Assert.That(cecilifiedCode, Does.Match(@"cls_myAttribute_\d+\.CustomAttributes.Add\(attr_my_\d+\);"), "Custom attribute should be be added");
        Assert.That(cecilifiedCode, Does.Match(@"var attr_my_\d+ = new CustomAttribute\(ctor_myAttribute_\d+\);"), "Reference to class declaring MyAttribute should be used when instantiating the custom attribute");
    }

    [Test]
    public void TestAttributeAppliedToAssembly()
    {
        var result = RunCecilifier($"[assembly:My(\"assembly\")] {AttributeDefinition}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(ClassDeclarationRegexFor("MyAttribute", "cls_myAttribute", ".+", "TypeAttributes.NotPublic")), "Expected TypeDefinition for attribute not found.");
        Assert.That(cecilifiedCode, Does.Match(@"var attr_my_1 = new CustomAttribute\(ctor_myAttribute_2\);"), "Reference to class declaring MyAttribute should be used when instantiating the custom attribute");
        Assert.That(cecilifiedCode, Does.Match(@"assembly.CustomAttributes.Add\(attr_my_1\);"), "Custom attribute should be be added to assembly");
    }

    [TestCase("class Foo<[My(\"Type\")] T> {{ }} {0}", TestName = "Type")]
    [TestCase("class Foo {{ void M<[My(\"Method\")] T>() {{ }} }} {0}", TestName = "Method")]
    public void TestAttributeAppliedToTypeParameters(string attributeUsage)
    {
        var result = RunCecilifier(string.Format(attributeUsage, AttributeDefinition));
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(@"var gp_T_\d+ = new Mono.Cecil.GenericParameter\(""T"", .+\);"));
        Assert.That(cecilifiedCode, Does.Match(@"gp_T_\d+.CustomAttributes.Add\(attr_my_\d+\);"));
    }

    [Test]
    public void TestGenericAttributeDefinition()
    {
        var result = RunCecilifier(GenericAttributeDefinition);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(?s)(cls_myGenericAttribute_\d+) = new TypeDefinition\(.+""MyGenericAttribute`1"".+,.+ImportReference\(typeof\(System.Attribute\)\)\);" +
            @"\s+.+(gp_T_\d+) = new Mono.Cecil.GenericParameter\(""T"", \1\);" +
            @"\s+.+\1.GenericParameters.Add\(\2\);"));
    }
    
    [TestCase("[MyGeneric<int>]", "Int32", TestName="Value Type")] 
    [TestCase("[MyGeneric<string>]", "String", TestName = "Reference Type")]
    [TestCase("[MyGeneric<Foo<int>>]", @"cls_foo_\d+.MakeGenericInstanceType\(.+Int32\)", TestName = "Generic Type")]
    public void TestGenericAttributeUsage(string attribute, string expectedType)
    {
        var result = RunCecilifier($@"
{GenericAttributeDefinition}

{attribute}
class Foo<TFoo> {{ }}");
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, 
            Does.Match(
                $@"(?s)var (attr_myGeneric_1_\d+) = new CustomAttribute\(new MethodReference\((ctor_myGenericAttribute_\d+)\.Name.+\2\.ReturnType\).+DeclaringType = cls_myGenericAttribute_\d+.MakeGenericInstanceType\(.*{expectedType}\).+\);\s+" + 
                @"cls_foo_\d+\.CustomAttributes\.Add\(\1\);"));        
    }
    
    [Test]
    public void ForwardReferenceToGenericAttributeWorks()
    {
        var result = RunCecilifier($$"""
                                        [MyGeneric<int>]
                                        class Foo<TFoo> { }
                                        {{GenericAttributeDefinition}}
                                        """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, 
            Does.Match("""
                       \s+assembly\.MainModule\.Types\.Add\((?<targetType>cls_foo_\d+)\);
                       \s+var (?<attr>attr_myGeneric_\d+_\d+) = new CustomAttribute\(.+(?<attrCtor>ctor_myGenericAttribute_\d+).Name, \k<attrCtor>.ReturnType\) {.+DeclaringType = cls_myGenericAttribute_\d+.MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\), .+\);
                       \s+cls_foo_22.CustomAttributes.Add\(\k<attr>\);
                       """));
    }
    
    [Test]
    public void ForwardReferenceToGenericAttributeWorks2()
    {
        var result = RunCecilifier($$"""
                                        class Foo 
                                        {
                                            [MyGeneric<int>]
                                            void M() {}
                                        }
                                        {{GenericAttributeDefinition}}
                                        """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, 
            Does.Match("""
                       \s+var (?<m>m_M_\d+) = new MethodDefinition\("M",.+\);
                       \s+cls_foo_\d+.Methods.Add\(\k<m>\);
                       \s+var (?<attr>attr_myGeneric_\d+_\d+) = new CustomAttribute\(.+(?<attrCtor>ctor_myGenericAttribute_\d+).Name, \k<attrCtor>.ReturnType\) {.+DeclaringType = cls_myGenericAttribute_\d+.MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\), .+\);
                       \s+\k<m>.CustomAttributes.Add\(\k<attr>\);
                       """));
    }
    
    [Test]
    public void CyclicForwardReferenceToGenericAttributeWorks1()
    {
        var result = RunCecilifier("""
                                        [MyGeneric<Foo>]
                                        class Foo { }
                                        
                                        class MyGenericAttribute<T> : System.Attribute {}
                                        """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, 
            Does.Match("""
                       \s+assembly\.MainModule\.Types\.Add\((?<targetType>cls_foo_\d+)\);
                       \s+var (?<attr>attr_myGeneric_\d+_\d+) = new CustomAttribute\(.+(?<attrCtor>ctor_myGenericAttribute_\d+).Name, \k<attrCtor>.ReturnType\) {.+DeclaringType = cls_myGenericAttribute_\d+.MakeGenericInstanceType\(\k<targetType>\), .+\);
                       \s+cls_foo_\d+.CustomAttributes.Add\(\k<attr>\);
                       """));
    }
    
    [Test]
    public void CyclicForwardReferenceToGenericAttributeWorks2()
    {
        var result = RunCecilifier("""
                                        [MyGeneric<int>]
                                        class Foo { }
                                        
                                        class MyGenericAttribute<T> : System.Attribute 
                                        {
                                            public Foo _foo;
                                        }
                                        """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, 
            Does.Match("""
                       //Class : Foo
                       \s+var (?<appliedTo>cls_foo_\d+) = new TypeDefinition\("", "Foo",.+\);
                       \s+assembly.MainModule.Types.Add\(\k<appliedTo>\);
                       
                       \s+//Class : MyGenericAttribute
                       \s+var (?<attrType>cls_myGenericAttribute_\d+) = new TypeDefinition\("", "MyGenericAttribute`1",.+ImportReference\(typeof\(System.Attribute\)\)\);
                       \s+var gp_T_\d+ = new Mono.Cecil.GenericParameter\("T", \k<attrType>\);
                       \s+\k<attrType>.GenericParameters.Add\(gp_T_\d+\);
                       \s+assembly.MainModule.Types.Add\(\k<attrType>\);
                       \s+var ctor_myGenericAttribute_4 = new MethodDefinition\(".ctor",.+TypeSystem.Void\);
                       \s+var (?<attrInstance>attr_myGeneric_1_\d+) = new CustomAttribute\(new MethodReference\(ctor_myGenericAttribute_4.Name, ctor_myGenericAttribute_4.ReturnType\) {.+DeclaringType = \k<attrType>.MakeGenericInstanceType\(.+Int32\).+}\);
                       \s+\k<appliedTo>.CustomAttributes.Add\(\k<attrInstance>\);
                       """));
    }

    [TestCase("LayoutKind.Auto, Pack=1, Size=12", 1, 12)]
    [TestCase("LayoutKind.Auto, Pack=1", 1, 0)]
    [TestCase("LayoutKind.Auto, Size=42", 0, 42)]
    [TestCase("LayoutKind.Sequential")]
    public void StructLayout_ItNotEmitted(string initializationData, int expectedPack = -1, int expectedSize = -1)
    {
        // StructLayout attribute should not be emitted to metadata as an attribute;
        // instead, the respective properties in the type definition should be set. 
        
        var result = RunCecilifier($$"""
                                   using System.Runtime.InteropServices;
                                   [StructLayout({{initializationData}})]
                                   struct Foo { }
                                   """);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Not.Match(@"st_foo_\d+.CustomAttributes.Add\(attr_structLayout_\d+\);"));

        if (expectedSize == -1 && expectedPack == -1)
        {
            Assert.That(cecilifiedCode, Does.Not.Match(@"st_foo_\d+.ClassSize = \d+;"));
            Assert.That(cecilifiedCode, Does.Not.Match(@"st_foo_\d+.PackingSize = \d+;"));
        }
        else
        {
            Assert.That(cecilifiedCode, Does.Match(@$"st_foo_\d+.ClassSize = {expectedSize};"));
            Assert.That(cecilifiedCode, Does.Match(@$"st_foo_\d+.PackingSize = {expectedPack};"));
        }
        
        Assert.That(cecilifiedCode, Does.Match($@"\|\s+TypeAttributes.{Regex.Match(initializationData, @"LayoutKind\.([^,$]+)").Groups[1].Value}Layout"));
    }

    private const string AttributeDefinition = "class MyAttribute : System.Attribute { public MyAttribute(string message) {} } ";
    private const string GenericAttributeDefinition = @"
                                [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple =true)]
                                class MyGenericAttribute<T> : System.Attribute 
                                { 
                                    public MyGenericAttribute() {} 
                                    public MyGenericAttribute(T value) {} 
                                    public T Value {get; set; } 
                                }";
}
