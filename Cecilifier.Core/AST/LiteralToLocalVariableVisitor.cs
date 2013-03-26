using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	internal class LiteralToLocalVariableVisitor : SyntaxRewriter
	{
		public override SyntaxNode VisitBlock(BlockSyntax block)
		{
			var methodDecl = EnclosingMethodDeclaration(block);
			if (methodDecl == null)
			{
				throw new NotSupportedException("Expansion of literals to locals outside methods not supported yet: " + block.ToFullString());
			}

			var transformedBlock = block;
			foreach (var literal in LiteralUsedAsMethodInvocationTarget(block))
			{
				transformedBlock = InsertLocalVariableStatementFor(literal, methodDecl.Identifier.ValueText, transformedBlock);
			}

			return base.VisitBlock(transformedBlock);
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

		private BlockSyntax InsertLocalVariableStatementFor(LiteralExpressionSyntax literal, string methodName, BlockSyntax block)
		{
			var typeSyntax = varTypeSyntax(block);
			var lineSpan = literal.SyntaxTree.GetLineSpan(literal.Span, true);

			var varDecl = Syntax.VariableDeclaration(typeSyntax, Syntax.SeparatedList(VariableDeclaratorFor(literal, string.Format("{0}_{1}", lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character), methodName)));

			var newBlockBody = InsertLocalVariableDeclarationInto(block, varDecl);

			return block.WithStatements(newBlockBody);
		}

		private static TypeSyntax varTypeSyntax(BlockSyntax block)
		{
			return Syntax.ParseTypeName("var").WithLeadingTrivia(block.ChildNodes().First().GetLeadingTrivia());
		}

		private static SyntaxList<StatementSyntax> InsertLocalVariableDeclarationInto(BlockSyntax block, VariableDeclarationSyntax varDecl)
		{
			var newBlockBody = Syntax.List(
				block.Statements
				     .TakeWhile(stmt => stmt.Kind == SyntaxKind.LocalDeclarationStatement)
				     .Concat(new[] {Syntax.LocalDeclarationStatement(varDecl).WithNewLine()})
				     .Concat(block.Statements.Where(stmt => stmt.Kind != SyntaxKind.LocalDeclarationStatement)));

			return newBlockBody;
		}

		private VariableDeclaratorSyntax VariableDeclaratorFor(LiteralExpressionSyntax literal, string typeName, string context)
		{
			var localVariableName = LocalVarNameFor(literal, typeName, context);
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

		private static string LocalVarNameFor(LiteralExpressionSyntax literal, string typeName, string context)
		{
			return string.Format("{0}_{1}_{2}", context, typeName, Normalize(literal.ToString()));
		}

		private static string Normalize(string value)
		{
			return Regex.Replace(value, @"\.", "_");
		}

		private static MethodDeclarationSyntax EnclosingMethodDeclaration(BlockSyntax block)
		{
			return (MethodDeclarationSyntax)block.Ancestors().Where(anc => anc.Kind == SyntaxKind.MethodDeclaration).SingleOrDefault();
		}

		private readonly IDictionary<string, string> literalToLocalVariable = new Dictionary<string, string>();
	}
}