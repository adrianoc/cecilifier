using System;
using System.Linq;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class NoCapturedVariableValidatorTests
{
    [TestCase("class Foo { void M(Foo foo) { bool Capture() => foo == null; } }", TestName = "Parent Parameter")]
    [TestCase("class Foo { void M(Foo foo) { void Capture() => foo.M(null); } }", TestName = "Method Invocation on captured parameter")]
    [TestCase("class Foo { void M(int parameter) { void Capture() { parameter++; } } }", TestName = "Parameter")]
    [TestCase("class Foo { void M() { int local = 42; void Capture() { local++; } } }", TestName = "Local")]
    [TestCase("class Foo { void M(string s) { int Capture() => s.Length; } }", TestName = "Property access on captured parameter")]
    public void LocalFunctions_Positive(string source)
    {
        var ctx = ParseAndCreateContextFor(source);
        var nodeToTest = ctx.SemanticModel.SyntaxTree.GetRoot().DescendantNodes().Single(node => node.IsKind(SyntaxKind.LocalFunctionStatement));
        
        Assert.That(NoCapturedVariableValidator.IsValid(ctx, nodeToTest), Is.False);
        Assert.That(ctx.Output, Does.Match(@"Local function that captures context are not supported. Node '.+ Capture\(\).+' captures .+"));
    }
    
    [TestCase("class Foo { void M() { void NonCapture(int parameter) { parameter++; } } }", TestName = "Parameter")]
    [TestCase("class Foo { void M() { void NonCapture() { int local = 42; local++; } } }", TestName = "Local")]
    [TestCase("class Foo { int field; void M() { void NonCapture() { field++; } } }", TestName = "Field")]
    [TestCase("class Foo { void M() { int NonCapture(Bar bar) => bar.value; } } struct Bar { public int value; }", TestName = "Member Access (field on parameter)")]
    [TestCase("class Foo { void M() { int NonCapture(Bar[] bars) => bars[0].value; } } struct Bar { public int value; }", TestName = "Member Access (field on array)")]
    [TestCase("class Foo { void M() { int NonCapture() { Bar bar = new Bar(); return bar.Get(); } } } struct Bar { public int value; public int Get() => 42; }", TestName = "Member Access (method on local)")]
    public void LocalFunctions_FalsePositive(string source)
    {
        var ctx = ParseAndCreateContextFor(source);
        var nodeToTest = ctx.SemanticModel.SyntaxTree.GetRoot().DescendantNodes().Single(node => node.IsKind(SyntaxKind.LocalFunctionStatement));
        
        Assert.That(NoCapturedVariableValidator.IsValid(ctx, nodeToTest), Is.True, ctx.Output);
        Assert.That(ctx.Output, Does.Not.Contain("Local function that captures context are not supported"));
    }
    
    private IVisitorContext ParseAndCreateContextFor(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var comp = CSharpCompilation.Create(null, new[] { syntaxTree }, new[] { MetadataReference.CreateFromFile(typeof(Func<>).Assembly.Location) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostics = comp.GetDiagnostics();

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
            throw new Exception(errors.Aggregate("", (acc, curr) => acc + curr.GetMessage() + Environment.NewLine));

        var context = new MonoCecilContext(new CecilifierOptions(), comp.GetSemanticModel(syntaxTree), indentation: 3);
        DefaultParameterExtractorVisitor.Initialize(context);
        UsageVisitor.ResetInstance();
        
        return context;
    }
}
