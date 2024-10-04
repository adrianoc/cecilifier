using System;
using System.Text.RegularExpressions;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture(Category = "TypeTests")]
public class RecordTests : CecilifierUnitTestBase
{
    [TestCase("struct")]
    [TestCase("class")]
    [TestCase(null)]
    public void SimplestRecordDeclaration(string classOrStruct)
    {
        var result = RunCecilifier($"public record {classOrStruct} TheRecord;");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match($"//Record {(classOrStruct ?? "class").PascalCase()} : TheRecord"));

        var declarationRegex = classOrStruct == "struct"
            ? StructDeclarationRegexFor("TheRecord", "rec_theRecord")
            : ClassDeclarationRegexFor("TheRecord", "rec_theRecord");
        
        Assert.That(cecilifiedCode, Does.Match(declarationRegex));
    }
    
    [TestCase("class", TestName = "Class explicit")]
    [TestCase("", TestName = "Class implicit")]
    [TestCase("struct", TestName = "struct")]
    public void EqualityContractProperty_WhenReferenceRecord_IsEmitted(string kind)
    {
        var result = RunCecilifier($"public record {kind} TheRecord(bool Value);");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        if (kind == "struct")
        {
            // No `EqualityContract` property for structs
            Assert.That(cecilifiedCode, Does.Not.Match("""new PropertyDefinition\("EqualityContract",.+\);"""));
            return;
        }
        
        // Assert that for `class records` we emit a `EqualityContract` property.
        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+var (prop_equalityContract_\d+) = new PropertyDefinition\("EqualityContract", PropertyAttributes.None, assembly.MainModule.ImportReference\(typeof\(System.Type\)\)\);
                                               \s+(rec_theRecord_\d+).Properties.Add\(\1\);
                                               \s+var (?<ecget>m_equalityContract_get_\d+) = new MethodDefinition\("get_EqualityContract", .+Family \| .+HideBySig \| .+SpecialName \| .+NewSlot | .+Virtual, .+ImportReference\(typeof\(System.Type\)\)\);
                                               \s+\2.Methods.Add\(\k<ecget>\);
                                               \s+\k<ecget>.Body = new MethodBody\(\k<ecget>\);
                                               \s+\1.GetMethod = \k<ecget>;
                                               \s+var il_equalityContract_get_\d+ = \k<ecget>.Body.GetILProcessor\(\);
                                               \s+\k<ecget>.Body = new MethodBody\(\k<ecget>\);
                                               \s+var il_equalityContract_get_\d+ = \k<ecget>.Body.GetILProcessor\(\);
                                               \s+var l_m_equalityContract_get_2_4 = \k<ecget>.Body.Instructions;
                                               \s+l_m_equalityContract_get_2_4.Add\(il_equalityContract_get_3.Create\(OpCodes.Ldtoken, \2\)\);
                                               \s+l_m_equalityContract_get_2_4.Add\(il_equalityContract_get_3.Create\(OpCodes.Call, .+assembly.MainModule.ImportReference\(TypeHelpers.ResolveMethod\(typeof\(System.Type\), "GetTypeFromHandle",.+\)\)\)\);
                                               \s+l_m_equalityContract_get_2_4.Add\(il_equalityContract_get_3.Create\(OpCodes.Ret\)\);
                                               """));
    }
    
    [TestCase("struct")]
    [TestCase("class")]
    [TestCase(null)]
    public void RecordType_Implements_IEquatable(string classOrStruct)
    {
        var result = RunCecilifier($"public record {classOrStruct} TheRecord(int Value);");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match($"//Record {(classOrStruct ?? "class").PascalCase()} : TheRecord"));
        Assert.That(cecilifiedCode, Does.Match("""
                                               	(?<recVar>rec_theRecord_\d+)\.Interfaces\.Add\(new InterfaceImplementation\(.+ImportReference\(typeof\(System.IEquatable<>\)\).MakeGenericInstanceType\(\k<recVar>\)\)\);
                                               	\s+assembly.MainModule.Types.Add\(\k<recVar>\);
                                               """));
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               //IEquatable<>.Equals\(TheRecord other\)
                                               \s+var (?<eq>m_equals_\d+) = new MethodDefinition\("Equals", (MethodAttributes.)Public \| \1HideBySig \| \1NewSlot \| \1Virtual, .+TypeSystem.Boolean\);
                                               \s+var (?<param>p_other_\d+) = new ParameterDefinition\("other", ParameterAttributes.None, (?<rec_var>rec_theRecord_\d+)\);
                                               \s+\k<eq>.Parameters.Add\(\k<param>\);
                                               \s+\k<rec_var>.Methods.Add\(\k<eq>\);
                                               """));
    }

    [Test]
    public void PrimaryConstructorParameters_AreMappedToPublicProperties1()
    {
        var result = RunCecilifier("public record TheRecord(int Value, TheRecord Parent, char Ch = '?');");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        AssertPropertiesFromPrimaryConstructor(["Value:assembly.MainModule.TypeSystem.Int32", @"Parent:rec_theRecord_\d+", "Ch:assembly.MainModule.TypeSystem.Char"], cecilifiedCode);
    }
    
    [Test]
    public void PrimaryConstructorParameters_AreMappedToPublicProperties2()
    {
        var result = RunCecilifier("public record TheRecord<T>(T Value, TheRecord<T> Other);");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        AssertPropertiesFromPrimaryConstructor(["Value:gp_T_1", @"Other:rec_theRecord_\d+.MakeGenericInstanceType\(gp_T_\d+\)"], cecilifiedCode);
    }
    
    [Test]
    public void PropertiesAreNotEmitted_WhenMatchesPropertyFromBaseRecord()
    {
        var result = RunCecilifier("public record BaseRecord<T>(T Value); public record DerivedRecord(string Name, int Value) : BaseRecord<int>(Value);");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.Multiple(() =>
        {
            var message = """
                              More than one `value getter/init` were generated. 
                              Most likely properties for all parameters of DerivedRecord were generated whereas only properties for `Name`
                              should have been emitted.
                              """;
            
            Assert.That(Regex.Matches(cecilifiedCode, "//Value getter").Count, Is.EqualTo(1), message);
            Assert.That(Regex.Matches(cecilifiedCode, "//Value init").Count, Is.EqualTo(1), message);
        });
    }
    
    [Test]
    public void ObjectAsBase_BaseConstructor_IsInvoked()
    {
        var result = RunCecilifier("public record SimpleRecord(int Value);");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match( """il_ctor_SimpleRecord_\d+.Emit\(OpCodes.Call, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), ".ctor",.+\)\)\);"""));
    }
    
    [Test]
    public void InheritingFromRecord_BaseConstructor_IsInvoked()
    {
        var result = RunCecilifier("public record BaseRecord(int Value); public record DerivedRecord(string Name, int Value) : BaseRecord(Value);");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match( """
                                                \s+il_ctor_DerivedRecord_\d+.Emit\(OpCodes\.Ldarg_0\);
                                                \s+il_ctor_DerivedRecord_\d+.Emit\(OpCodes.Ldarg_2\);
                                                \s+il_ctor_DerivedRecord_\d+.Emit\(OpCodes.Call, ctor_baseRecord_\d+\);
                                                """));
    }

    [Test]
    public void Members_HaveCompilerGeneratedAttribute_Added()
    {
        var result = RunCecilifier("public record Record(int Value);");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match( """m_deconstruct_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Deconstruct() method.");
        Assert.That(cecilifiedCode, Does.Match( """prop_equalityContract_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "EqualityContract");
        Assert.That(cecilifiedCode, Does.Match( """m_equals_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Equals()");
        Assert.That(cecilifiedCode, Does.Match( """m_printMembers_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "PrintMembers()");
        Assert.That(cecilifiedCode, Does.Match( """m_toString_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "ToString()");
        Assert.That(cecilifiedCode, Does.Match( """m_getHashCode_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "GetHashCode()");
        Assert.That(cecilifiedCode, Does.Match( """m_equalsObjectOverload_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Equals(object)");
        Assert.That(cecilifiedCode, Does.Match( """m_equalsOperator_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Operator ==");
        Assert.That(cecilifiedCode, Does.Match( """m_inequalityOperator_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Operator !=");
        Assert.That(cecilifiedCode, Does.Match( """m_getValue_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Value property getter");
        Assert.That(cecilifiedCode, Does.Match( """m_setValue_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Value property setter");
        Assert.That(cecilifiedCode, Does.Match( """fld_value_\d+.CustomAttributes.Add\(attr_compilerGenerated_\d+\);"""), "Value property setter");
    }

    private static void AssertPropertiesFromPrimaryConstructor(string[] expectedNameTypePairs, string cecilifiedCode)
    {
        Span<Range> ranges = stackalloc Range[2];
        foreach (var pair in expectedNameTypePairs)
        {
            var expected = pair.AsSpan();
            var splitted = expected.Split(ranges, ':', StringSplitOptions.TrimEntries);
            Assert.That(splitted, Is.EqualTo(2));

            var propertyName = expected[ranges[0]];
            var propertyType = expected[ranges[1]];
            
            Assert.That(
                cecilifiedCode, 
                Does.Match($"""
                            //Property: {propertyName} \(primary constructor\)
                            \s+var (?<prop_var>prop_{Char.ToLower(propertyName[0])}{propertyName.Slice(1)}_\d+) = new PropertyDefinition\("{propertyName}", PropertyAttributes.None, {propertyType}\);
                            \s+rec_theRecord_\d+.Properties.Add\(\k<prop_var>\);
                            """));
            
            // ensure a method was generated for the `get` accessor
            Assert.That(cecilifiedCode, Does.Match($"""
                                                    //{propertyName} getter
                                                    \s+var (m_get{propertyName}_\d+) = new MethodDefinition\("get_{propertyName}", (MethodAttributes\.)Public \| \2HideBySig \| \2SpecialName, {propertyType}\);
                                                    \s+rec_theRecord_\d+.Methods.Add\(\1\);
                                                    """));
        
            // ensure a method was generated for the `init` accessor
            Assert.That(cecilifiedCode, Does.Match($"""
                                                    //{propertyName} init
                                                    \s+var (m_set{propertyName}_\d+) = new MethodDefinition\("set_{propertyName}", (MethodAttributes\.)Public \| \2HideBySig \| \2SpecialName, new RequiredModifierType\(.+ImportReference\(typeof\(.+IsExternalInit\)\), .+Void\)\);
                                                    \s+var (p_value_\d+) = new ParameterDefinition\("value", ParameterAttributes.None, {propertyType}\);
                                                    \s+\1.Parameters.Add\(\3\);
                                                    \s+rec_theRecord_\d+.Methods.Add\(\1\);
                                                    """));            
        }
    }
}
