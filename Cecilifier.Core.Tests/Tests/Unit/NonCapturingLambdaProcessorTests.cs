using System;
using System.Collections;
using System.Linq;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixtureSource(typeof(GeneratorApiDriverProvider))]
    public class NonCapturingLambdaProcessorTests(IILGeneratorApiDriver apiDriver) : MultipleILGeneratorApiDriverTest(apiDriver)
    {
        [TestCase("class Foo { System.Func<string, int> Bar() => s => s.Length; }", TestName = "Simple Lambda Expression")]
        [TestCase("class Foo { System.Func<string, int> Bar() => (s) => s.Length; }", TestName = "Parenthesized Lambda Expression")]
        [TestCase("class Foo { System.Func<int> Bar() => () => 42; }", TestName = "No Parameters")]
        public void BasicTests(string source)
        {
            var context = RunProcessorOn(source);
            Assert.That(context.Output, Contains.Substring("/Synthetic method for lambda expression"));
        }

        [TestCase("class Foo { void Bar(int fromParentMethod) { System.Func<int, int> fi = i => i + fromParentMethod ; }  }", "//Anonymous method / lambda that captures context are not supported. Node 'i => i + fromParentMethod' captures fromParentMethod", TestName = "Capture Parameter")]
        [TestCase("class Foo { int field; System.Func<int, int> Bar() => i => i + field; }", "//Anonymous method / lambda that captures context are not supported. Node 'i => i + field' captures field", TestName = "Capture Field")]
        [TestCase("class Foo { void Bar() { int local = 10; System.Func<int, int> fi = i => i + local ; }  }", "//Anonymous method / lambda that captures context are not supported. Node 'i => i + local' captures local", TestName = "Capture Local")]
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

        [TestCase("class Foo { delegate int D(int i); void Bar() { D fi = i => i + 1; }  }", TestName = "Non Generic Delegate")]
        [TestCase("class Foo { delegate T D<T>(T i); void Bar() { D<int> fi = i => i + 1; }  }", TestName = "Generic Delegate")]
        public void Conversion_FromDelegates_OtherThanFuncAndAction_AreReported(string source)
        {
            var context = RunProcessorOn(source);
            Assert.That(context.Output, Does.Not.Contains("//Synthetic method for lambda expression: i => i + 1"));
            Assert.That(context.Output, Contains.Substring("#warning Lambda to delegates conversion is only supported for Func<> and Action<>"));
        }

        [TestCase("using System; class Foo { void M(Func<int, int> a) { M(x => x + 1); } }", TestName = "Expression")]
        [TestCase("using System; class Foo { void M(Func<int, int> a) { M(x => { return x + 1; } ); } }", TestName = "Statement")]
        public void LambdaBodyIsProcessed(string source)
        {
            var context = RunProcessorOn(source);
            Assert.That(
                context.Output,
                Does.Match(@"(il_lambda_.+\.Emit\(OpCodes\.)Ldarg_0\);\s+" +
                           @"\1Ldc_I4, 1\);\s+" +
                           @"\1Add\);\s+" +
                           @"\1Ret\);"));
        }

        [TestCaseSource(nameof(Issue_176_TestScenarios))]
        public void Issue_176(string lambdaDeclaration, string expectedSnippet)
        {
            var context = RunProcessorOn($@"using System; class Foo {{ void Bar() {{ {lambdaDeclaration} }} }}");
            Assert.That(context.Output, Contains.Substring(expectedSnippet));
        }

        static IEnumerable Issue_176_TestScenarios()
        {
            return new[]
            {
                new TestCaseData(
                    @"Action<int> a = i => { int l = 42; };",

                    @"var l_l_3 = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);
			m_lambda_0_55_0.Body.Variables.Add(l_l_3);
			il_lambda_0_55_2.Emit(OpCodes.Ldc_I4, 42);
			il_lambda_0_55_2.Emit(OpCodes.Stloc, l_l_3);
			il_lambda_0_55_2.Emit(OpCodes.Ret);").SetName("Lambda Without Explicit Return"),

                new TestCaseData(
                    @"Func<int, int> f = i => { int l = i; return l; };",

                    @"//int l = i;
			var l_l_3 = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);
			m_lambda_0_58_0.Body.Variables.Add(l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ldarg_0);
			il_lambda_0_58_2.Emit(OpCodes.Stloc, l_l_3);

			//return l;
			il_lambda_0_58_2.Emit(OpCodes.Ldloc, l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ret);").SetName("Local Variable Initialization"),

                new TestCaseData(
                    @"Func<int, int> f = i => { int l; l = i; return l; };",

                    @"//int l;
			var l_l_3 = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);
			m_lambda_0_58_0.Body.Variables.Add(l_l_3);

			//l = i;
			il_lambda_0_58_2.Emit(OpCodes.Ldarg_0);
			il_lambda_0_58_2.Emit(OpCodes.Stloc, l_l_3);

			//return l;
			il_lambda_0_58_2.Emit(OpCodes.Ldloc, l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ret);").SetName("Local Variable Assignment"),

                new TestCaseData(
                    @"Func<int, int> f = i => { int l = i + 1; return l; };",

                    @"//int l = i + 1;
			var l_l_3 = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);
			m_lambda_0_58_0.Body.Variables.Add(l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ldarg_0);
			il_lambda_0_58_2.Emit(OpCodes.Ldc_I4, 1);
			il_lambda_0_58_2.Emit(OpCodes.Add);
			il_lambda_0_58_2.Emit(OpCodes.Stloc, l_l_3);

			//return l;
			il_lambda_0_58_2.Emit(OpCodes.Ldloc, l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ret);").SetName("Local Variable Initialization With Expression"),

                new TestCaseData(
                    @"Func<int, int> f = i => { int l = i; l = l + 1; return l; };",

                    @"//int l = i;
			var l_l_3 = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);
			m_lambda_0_58_0.Body.Variables.Add(l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ldarg_0);
			il_lambda_0_58_2.Emit(OpCodes.Stloc, l_l_3);

			//l = l + 1;
			il_lambda_0_58_2.Emit(OpCodes.Ldloc, l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ldc_I4, 1);
			il_lambda_0_58_2.Emit(OpCodes.Add);
			il_lambda_0_58_2.Emit(OpCodes.Stloc, l_l_3);

			//return l;
			il_lambda_0_58_2.Emit(OpCodes.Ldloc, l_l_3);
			il_lambda_0_58_2.Emit(OpCodes.Ret);").SetName("Local Variable In Expression"),
            };
        }


        private CecilifierContextBase RunProcessorOn(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var comp = CSharpCompilation.Create(null, new[] { syntaxTree }, new[] { MetadataReference.CreateFromFile(typeof(Func<>).Assembly.Location) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var diagnostics = comp.GetDiagnostics();

            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
            if (errors.Any())
                throw new Exception(errors.Aggregate("", (acc, curr) => acc + curr.GetMessage() + Environment.NewLine));

            var context = new MonoCecilContext(new CecilifierOptions(), comp.GetSemanticModel(syntaxTree), indentation: 3);
            DefaultParameterExtractorVisitor.Initialize(context);
            UsageVisitor.ResetInstance();

            context.DefinitionVariables.RegisterNonMethod("Foo", "field", VariableMemberKind.Field, "fieldVar"); // Required for Field tests
            NonCapturingLambdaProcessor.InjectSyntheticMethodsForNonCapturingLambdas(
                context,
                syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().SingleOrDefault(),
                "");
            return context;
        }
    }
}
