using System;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class NonCapturingLambdaProcessorTests
    {
        [TestCase("class Foo { System.Func<string, int> Bar() => s => s.Length; }", TestName = "Simple Lambda Expression")]
        [TestCase("class Foo { System.Func<string, int> Bar() => (s) => s.Length; }", TestName = "Parenthesized Lambda Expression")]
        [TestCase("class Foo { System.Func<int> Bar() => () => 42; }", TestName = "No Parameters")]
        public void BasicTests(string source)
        {
            var context = RunProcessorOn(source);
            Assert.That(context.Output, Contains.Substring("/Synthetic method for lambda expression"));
        }
        
        [TestCase("class Foo { void Bar(int fromParentMethod) { System.Func<int, int> fi = i => i + fromParentMethod ; }  }", "//Lambdas that captures context are not supported. Lambda expression 'i => i + fromParentMethod' captures fromParentMethod", TestName = "Capture Parameter")]
        [TestCase("class Foo { int field; System.Func<int, int> Bar() => i => i + field; }", "//Lambdas that captures context are not supported. Lambda expression 'i => i + field' captures field", TestName = "Capture Field")]
        [TestCase("class Foo { void Bar() { int local = 10; System.Func<int, int> fi = i => i + local ; }  }", "//Lambdas that captures context are not supported. Lambda expression 'i => i + local' captures local", TestName = "Capture Local")]
        public void Captured_Variables_AreDetected(string source, string expected)
        {
            var context = RunProcessorOn(source);

            Assert.That(context.Output, Contains.Substring(expected));
            Assert.That(context.Output, Does.Not.Contains("/Synthetic method for lambda expression"));
        }

        [Test]
        public void FalsePositives_Captured_Variables_AreNotReported()
        {
            var context = RunProcessorOn("class Foo { void Bar() { System.Func<int, int> fi = i => { int local = 10; return i + local ; }; }  }");

            Assert.That(context.Output, Does.Not.Contains("//Lambdas that captures context are not supported"));
            Assert.That(context.Output, Contains.Substring("/Synthetic method for lambda expression"));
        }

        [TestCase("class Foo { delegate int D(int i); void Bar() { D fi = i => i + 1; }  }", TestName= "Non Generic Delegate")]
        [TestCase("class Foo { delegate T D<T>(T i); void Bar() { D<int> fi = i => i + 1; }  }", TestName= "Generic Delegate")]
        public void Conversion_FromDelegates_OtherThanFuncAndAction_AreReported(string source)
        {
            var context = RunProcessorOn(source);
            Assert.That(context.Output, Does.Not.Contains("//Synthetic method for lambda expression: i => i + 1"));
            Assert.That(context.Output, Contains.Substring("//Lambda to delegates conversion is only supported for Func<> and Action<>"));
        }
        
        private static CecilifierContext RunProcessorOn(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var comp = CSharpCompilation.Create(null, new[] { syntaxTree }, new[] { MetadataReference.CreateFromFile(typeof(Func<>).Assembly.Location) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var diagnostics = comp.GetDiagnostics();
            
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
            if (errors.Any())
                throw new Exception(errors.Aggregate("", (acc, curr) => acc + curr.GetMessage() + Environment.NewLine));
            
            var context = new CecilifierContext(comp.GetSemanticModel(syntaxTree), new CecilifierOptions(), -1);

            context.DefinitionVariables.RegisterNonMethod("Foo", "field", MemberKind.Field, "fieldVar"); // Required for Field tests
            NonCapturingLambdaProcessor.InjectSyntheticMethodsForNonCapturingLambdas(
                context,
                syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().SingleOrDefault(),
                "");
            return context;
        }
    }
}
