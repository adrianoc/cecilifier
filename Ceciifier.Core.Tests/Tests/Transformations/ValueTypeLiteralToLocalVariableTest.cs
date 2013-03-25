using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ceciifier.Core.Tests.Framework;
using Cecilifier.Core.Extensions;
using NUnit.Framework;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Ceciifier.Core.Tests.Tests.Transformations
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
				                                            new LiteralToLocalVariableVisitor(comp.GetSemanticModel(syntaxTree)));

				var expectedTree = SyntaxTree.ParseText(expected.ReadToEnd());

				Assert.AreEqual(
					expectedTree.ToString(),
					transformedTree.ToString(),
					string.Format("Expected: {0}\r\n---------------- got -------------------\r\n{1}\r\n", expectedTree, transformedTree));
			}
		}

		private static SyntaxTree ApplyTransformationTo(SyntaxTree tree, LiteralToLocalVariableVisitor visitor)
		{
			CompilationUnitSyntax root;
			tree.TryGetRoot(out root);

			var transformedTree = visitor.Run(root);
			return transformedTree;
		}
	}

	internal class LiteralToLocalVariableVisitor : SyntaxRewriter
	{
		public SyntaxTree Run(CompilationUnitSyntax root)
		{

			CompilationUnitSyntax newRoot;
			do
			{
				newRoot = (CompilationUnitSyntax) root.Accept(this);
				root = newRoot ?? root;
			} while (newRoot != null);

			return SyntaxTree.Create(root);
		}

		public LiteralToLocalVariableVisitor(SemanticModel semanticModel)
		{
			this.semanticModel = semanticModel;
		}

		public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			var old = node.Expression as LiteralExpressionSyntax;
			if (old != null)
			{
				var localVarIdentifier = Syntax.Identifier(literalToLocalVariable[old.Token.ToString()])
											.WithLeadingTrivia(old.GetLeadingTrivia())
											.WithTrailingTrivia(old.GetTrailingTrivia());

				return Syntax.MemberAccessExpression(SyntaxKind.MemberAccessExpression, Syntax.IdentifierName(localVarIdentifier), node.Name);

			}

			return base.VisitMemberAccessExpression(node);
		}

		public override SyntaxNode VisitBlock(BlockSyntax block)
		{
			var transformedBlock = block;
			var toBeFixed = LiteralUsedAsMethodInvocationTarget(block);
			foreach (var literal in toBeFixed)
			{
				transformedBlock = transformedBlock.WithStatements(AddLocalVariableStatementFor(block, literal));
			}

			return base.VisitBlock(transformedBlock);
		}

		private SyntaxList<StatementSyntax> AddLocalVariableStatementFor(BlockSyntax block, LiteralExpressionSyntax literal)
		{
			var methodDecl = (MethodDeclarationSyntax) block.Ancestors().Where(anc => anc.Kind == SyntaxKind.MethodDeclaration).SingleOrDefault();
			if (methodDecl == null) return block;


			var literalType = semanticModel.GetTypeInfo(literal).Type;
			var literalTypeName = literalType.ToMinimalDisplayString(literal.GetLocation(), semanticModel);
			
			var typeSyntax = Syntax.ParseTypeName(literalTypeName).WithLeadingTrivia(block.ChildNodes().First().GetLeadingTrivia());
			var localVar = VariableDeclaratorFor(literal, methodDecl, literalTypeName);

			var constM = Syntax.VariableDeclaration(typeSyntax, Syntax.SeparatedList(localVar));

			//return Syntax.List(
			//	block.Statements
			//			.TakeWhile(stmt => stmt.Kind == SyntaxKind.LocalDeclarationStatement)
			//			.Concat(new[] { Syntax.LocalDeclarationStatement(constM).WithNewLine() })
			//			.Concat( block.Statements.Where( stmt => stmt.Kind != SyntaxKind.LocalDeclarationStatement)));

			return Syntax.List(new[] { Syntax.LocalDeclarationStatement(constM).WithNewLine() }.Concat(block.Statements));
		}

		private VariableDeclaratorSyntax VariableDeclaratorFor(LiteralExpressionSyntax literal, MethodDeclarationSyntax methodDecl, string typeName)
		{
			var localVariableName = LocalVarNameFor(methodDecl, literal, typeName);
			literalToLocalVariable[literal.Token.ToString()] = localVariableName;

			var declarator = Syntax.VariableDeclarator(localVariableName).WithLeadingTrivia(Syntax.Space);
			return declarator.WithInitializer(Syntax.EqualsValueClause(InitializerTrivia(literal)).WithLeadingTrivia(Syntax.Space));
		}

		private static LiteralExpressionSyntax InitializerTrivia(LiteralExpressionSyntax literal)
		{
			return literal.ReplaceTrivia(literal.GetLeadingTrivia().AsEnumerable(), (trivia, syntaxTrivia) => Syntax.TriviaList(Syntax.Space));
		}

		private IList<LiteralExpressionSyntax> LiteralUsedAsMethodInvocationTarget(BlockSyntax block)
		{
			return block.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(litNode => litNode.Parent.Kind == SyntaxKind.MemberAccessExpression).ToList();
		}

		private string LocalVarNameFor(MethodDeclarationSyntax methodDecl, LiteralExpressionSyntax literal, string typeName)
		{
			return string.Format("{0}_{1}_{2}", methodDecl.Identifier.ToString(), typeName, literal);
		}


		private readonly SemanticModel semanticModel;
		private IDictionary<string, string> literalToLocalVariable = new Dictionary<string, string>();
	}
}
