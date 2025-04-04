using System;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class TypeSymbolExtensionsTests
{
    // Most types are indirectly covered by other tests.
    [TestCase("int[]", sizeof(int))]
    [TestCase("int", sizeof(int))]
    [TestCase("byte", sizeof(byte))]
    [TestCase("long", sizeof(long))]
    [TestCase("ref long", sizeof(long))]
    public void SizeofPrimitiveType_ReturnsSizeofPrimitiveType(string fieldType, int expectedSize)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText($"ref struct C {{ {fieldType} _field; }}");
        
        var comp = CSharpCompilation.Create("Cecilifier", [syntaxTree]);
        var semanticModel = comp.GetSemanticModel(syntaxTree);

        var fieldDeclaration = syntaxTree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single().Declaration.Variables.First();
        var fieldSymbol = semanticModel.GetDeclaredSymbol(fieldDeclaration).EnsureNotNull<ISymbol, IFieldSymbol>();
        
        Assert.That(fieldSymbol.Type, Is.Not.Null);
        Assert.That(fieldSymbol.Type.SizeofPrimitiveType(), Is.EqualTo(expectedSize));
    }
    
    [Test]
    public void SizeofPrimitiveType_WhenTypeIsNotPrimitive_Throws()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class C { object _field; }");
        
        var comp = CSharpCompilation.Create("Cecilifier", [syntaxTree]);
        var semanticModel = comp.GetSemanticModel(syntaxTree);

        var fieldDeclaration = syntaxTree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single().Declaration.Variables.First();
        var fieldSymbol = semanticModel.GetDeclaredSymbol(fieldDeclaration).EnsureNotNull<ISymbol, IFieldSymbol>();
        
        Assert.That(fieldSymbol.Type, Is.Not.Null);
        Assert.Throws(typeof(NotImplementedException), () => fieldSymbol.Type.SizeofPrimitiveType());
    }

    [TestCase("byte", "ldind.u1")]
    [TestCase("ushort", "ldind.u2")]
    [TestCase("uint", "ldind.u4")]
    [TestCase("ulong", "ldind.i8")]
    public void LdindOpCodeFor(string primitiveType, string expectedOpCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText($"class C {{ {primitiveType} _field; }}");
        
        var comp = CSharpCompilation.Create("Cecilifier", [syntaxTree]);
        var semanticModel = comp.GetSemanticModel(syntaxTree);

        var fieldDeclaration = syntaxTree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single().Declaration.Variables.First();
        var fieldSymbol = semanticModel.GetDeclaredSymbol(fieldDeclaration).EnsureNotNull<ISymbol, IFieldSymbol>();
        
        Assert.That(fieldSymbol.Type, Is.Not.Null);
        Assert.That(fieldSymbol.Type.LdindOpCodeFor().ToString(), Is.EqualTo(expectedOpCode));
    }
}
