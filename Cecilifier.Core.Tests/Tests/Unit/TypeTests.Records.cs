using System;
using Cecilifier.Core.Extensions;
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
    
    [TestCase("struct")]
    [TestCase("class")]
    [TestCase(null)]
    public void RecordType_Implements_IEquatable(string classOrStruct)
    {
        var result = RunCecilifier($"public record {classOrStruct} TheRecord;");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match($"//Record {(classOrStruct ?? "class").PascalCase()} : TheRecord"));
        Assert.That(cecilifiedCode, Does.Match("""
                                               	(?<recVar>rec_theRecord_\d+)\.Interfaces\.Add\(new InterfaceImplementation\(.+ImportReference\(typeof\(System.IEquatable<>\)\).MakeGenericInstanceType\(\k<recVar>\)\)\);
                                               	\s+assembly.MainModule.Types.Add\(\k<recVar>\);
                                               """));
    }

    [Test]
    public void PrimaryConstructorParameters_AreMappedToPublicProperties()
    {
        var result = RunCecilifier("public record TheRecord(int Value, TheRecord Parent, char Ch = '?');");

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Span<Range> ranges = stackalloc Range[2];
        
        foreach (var pair in new[] { "Value:assembly.MainModule.TypeSystem.Int32", @"Parent:rec_theRecord_\d+", "Ch:assembly.MainModule.TypeSystem.Char" })
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
