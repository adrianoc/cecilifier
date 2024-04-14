using Cecilifier.Core.Extensions;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public partial class TypeTests
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
}
