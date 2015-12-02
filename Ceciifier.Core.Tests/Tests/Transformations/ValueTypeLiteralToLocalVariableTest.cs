using System.IO;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

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
				var syntaxTree = SyntaxTree.ParseText(sourceReader.ReadToEnd());

				var comp = Compilation.Create(
					"Test",
					new CompilationOptions(OutputKind.DynamicallyLinkedLibrary),
					new[] {syntaxTree},
					new[] {MetadataReference.CreateAssemblyReference(typeof (object).Assembly.FullName)});

				var transformedTree = ApplyTransformationTo(syntaxTree,
				                                            new ValueTypeToLocalVariableVisitor());

				var expectedTree = SyntaxTree.ParseText(expected.ReadToEnd());

				Assert.AreEqual(
					expectedTree.ToString(),
					transformedTree.ToString(),
					string.Format("Expected: {0}\r\n---------------- got -------------------\r\n{1}\r\n", expectedTree, transformedTree));
			}
		}

		private static SyntaxTree ApplyTransformationTo(SyntaxTree tree, ValueTypeToLocalVariableVisitor visitor)
		{
			CompilationUnitSyntax root;
			tree.TryGetRoot(out root);

			return SyntaxTree.Create((CompilationUnitSyntax) root.Accept(visitor));
		}
	}
}
