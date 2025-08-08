using System.Linq;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class CecilifierContextTests
{
    [OneTimeSetUp]
    public void SetUpFixture()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class C {}");
        var comp = CSharpCompilation.Create("", new[] { syntaxTree });
        _semanticModel = comp.GetSemanticModel(syntaxTree);
    }
    
    [Test]
    public void WarningDiagnosticsAreEmitted()
    {
        var cecilifierContext = CreateContext();
        cecilifierContext.EmitWarning("Simple Warning");

        var found = cecilifierContext.Diagnostics.Where(d => d.Message.Contains("Simple Warning")).ToList();
        Assert.That(found, Is.Not.Null);
        Assert.That(found.Count, Is.EqualTo(1));
        Assert.That(found[0].Kind, Is.EqualTo(DiagnosticKind.Warning));
    }
    
    [Test]
    public void ErrorDiagnosticsAreEmitted()
    {
        var cecilifierContext = CreateContext();
        cecilifierContext.EmitError("Simple Error");

        var found = cecilifierContext.Diagnostics.Where(d => d.Message.Contains("Simple Error")).ToList();
        Assert.That(found, Is.Not.Null);
        Assert.That(found.Count, Is.EqualTo(1));
        Assert.That(found[0].Kind, Is.EqualTo(DiagnosticKind.Error));
    }
    
    [Test]
    public void DiagnosticsEmitsEquivalentPreprocessorDirectives()
    {
        var cecilifierContext = CreateContext();
        cecilifierContext.EmitError("Simple Error");
        cecilifierContext.EmitWarning("Simple Warning");

        Assert.That(cecilifierContext.Output, Contains.Substring("#error Simple Error"));
        Assert.That(cecilifierContext.Output, Contains.Substring("#warning Simple Warning"));
    }
    
    [Test]
    public void NewLinesInDiagnosticsEmitsMultiplePreprocessorDirectives()
    {
        var cecilifierContext = CreateContext();
        cecilifierContext.EmitWarning("Warning with\nmultiple\nlines");

        Assert.That(cecilifierContext.Output, Contains.Substring("#warning Warning with"));
        Assert.That(cecilifierContext.Output, Contains.Substring("#warning multiple"));
        Assert.That(cecilifierContext.Output, Contains.Substring("#warning lines"));
    }

    private CecilifierContext CreateContext() => new CecilifierContext(_semanticModel, new CecilifierOptions { GeneratorApiDriver = new MonoCecilGeneratorDriver() }, 0);

    private SemanticModel _semanticModel;
}
