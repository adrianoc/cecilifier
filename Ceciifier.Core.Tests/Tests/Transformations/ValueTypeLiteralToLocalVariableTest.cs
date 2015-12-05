using System.IO;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Transformations
{
	[TestFixture]
	public class ValueTypeLiteralToLocalVariableTest : ResourceTestBase
	{
		[Test]
		public void SingleValueTypeLiteralsArePromotedToLocalVariables()
		{
			AssertTransformation("SingleLiteralAsTargetOfCall");
		}

		[Test]
		public void MultipleValueTypeLiteralsArePromotedToLocalVariables()
		{
			AssertTransformation("MultipleLiteralAsTargetOfCall");
		}

		[Test]
		public void ValueTypeReturnAsTargetOfCall()
		{
			AssertTransformation("ValueTypeReturnAsTargetOfCall");
		}

		private void AssertTransformation(string resourceName)
		{
			var source = ReadResource(resourceName, "cs", TestKind.Transformation);

			using (var expected = new StreamReader(ReadResource(resourceName, "out", TestKind.Transformation)))
			using (var sourceReader = new StreamReader(source))
			{
				var syntaxTree = CSharpSyntaxTree.ParseText(sourceReader.ReadToEnd());

				var comp = CSharpCompilation.Create(
							"Test",
							new[] { syntaxTree },
							new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
							new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

				var transformedTree = ApplyTransformationTo(syntaxTree, new ValueTypeToLocalVariableVisitor(comp.GetSemanticModel(syntaxTree)));

				var expectedTree = CSharpSyntaxTree.ParseText(expected.ReadToEnd());

				Assert.AreEqual(
					expectedTree.ToString(),
					transformedTree.ToString(),
					string.Format("Expected: {0}\r\n---------------- got -------------------\r\n{1}\r\n", expectedTree, transformedTree));

			}
		}

		private static SyntaxTree ApplyTransformationTo(SyntaxTree tree, ValueTypeToLocalVariableVisitor visitor)
		{
			SyntaxNode root;
			tree.TryGetRoot(out root);

			return CSharpSyntaxTree.Create((CompilationUnitSyntax) ((CompilationUnitSyntax)root).Accept(visitor));
		}
	}
}
