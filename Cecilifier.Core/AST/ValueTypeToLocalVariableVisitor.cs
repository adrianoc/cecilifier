using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	internal class ValueTypeToLocalVariableVisitor : SyntaxRewriter
	{
		public override SyntaxNode VisitBlock(BlockSyntax block)
		{
			var callSitesToFix = InvocationsOnValueTypes(block);

			var methodDecl = EnclosingMethodDeclaration(block);
			if (methodDecl == null && callSitesToFix.Count > 0)
			{
				throw new NotSupportedException("Expansion of literals to locals outside methods not supported yet: " + block.ToFullString() + "  " + block.SyntaxTree.GetLineSpan(block.Span, true));
			}
			
			var transformedBlock = block;
			foreach (var callSite in callSitesToFix)
			{
				transformedBlock = InsertLocalVariableStatementFor(callSite, methodDecl.Identifier.ValueText, transformedBlock);
			}

			return base.VisitBlock(transformedBlock);
		}

		public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			if (node.Parent.Kind != SyntaxKind.InvocationExpression) return node;

			var newMae = node.Expression.Accept(new ValueTypeOnCallSiteFixer(node, callSiteToLocalVariable));

			return newMae ?? base.VisitMemberAccessExpression(node);
		}

		private BlockSyntax InsertLocalVariableStatementFor(ExpressionSyntax callSite, string methodName, BlockSyntax block)
		{
			var typeSyntax = VarTypeSyntax(block);
			var lineSpan = callSite.SyntaxTree.GetLineSpan(callSite.Span, true);

			var varDecl = Syntax.VariableDeclaration(typeSyntax, Syntax.SeparatedList(VariableDeclaratorFor(callSite, string.Format("{0}_{1}", lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character), methodName)));

			var newBlockBody = InsertLocalVariableDeclarationIntoBeforeCallSite(block, varDecl, callSite);

			return block.WithStatements(newBlockBody);
		}

		private static TypeSyntax VarTypeSyntax(BlockSyntax block)
		{
			return Syntax.ParseTypeName("var").WithLeadingTrivia(block.ChildNodes().First().GetLeadingTrivia());
		}

		private static SyntaxList<StatementSyntax> InsertLocalVariableDeclarationIntoBeforeCallSite(BlockSyntax block, VariableDeclarationSyntax varDecl, ExpressionSyntax callSite)
		{
			var stmts = block.Statements;

			var callSiteStatement = callSite.Ancestors().Where(node => node.Kind == SyntaxKind.ExpressionStatement).First();

			var i = stmts.IndexOf(stmt => stmt.EquivalentTo(callSiteStatement));
			
			var newStmts = stmts.Insert(
									i, 
									new StatementSyntax[] { Syntax.LocalDeclarationStatement(varDecl).WithNewLine() });

			return Syntax.List(newStmts.AsEnumerable());
		}

		private VariableDeclaratorSyntax VariableDeclaratorFor(ExpressionSyntax callSite, string typeName, string context)
		{
			var localVariableName = LocalVarNameFor(callSite, typeName, context);
			callSiteToLocalVariable[callSite.ToString()] = localVariableName;

			var declarator = Syntax.VariableDeclarator(localVariableName).WithLeadingTrivia(Syntax.Space);
			return declarator.WithInitializer(Syntax.EqualsValueClause(InitializeTrivia(callSite)).WithLeadingTrivia(Syntax.Space));
		}

		private static T InitializeTrivia<T>(T callSite) where T : SyntaxNode
		{
			return callSite.ReplaceTrivia(callSite.GetLeadingTrivia().AsEnumerable(), (trivia, syntaxTrivia) => Syntax.TriviaList(Syntax.Space));
		}

		private IList<ExpressionSyntax> InvocationsOnValueTypes(BlockSyntax block)
		{
			return MemberAccessOnValueTypeCollectorVisitor.Collect(block);
		}

		private static string LocalVarNameFor(ExpressionSyntax callSite, string typeName, string context)
		{
			return string.Format("{0}_{1}_{2}", context, typeName, callSite.Accept(NameExtractorVisitor.Instance));
		}

		private static MethodDeclarationSyntax EnclosingMethodDeclaration(BlockSyntax block)
		{
			return (MethodDeclarationSyntax)block.Ancestors().Where(anc => anc.Kind == SyntaxKind.MethodDeclaration).SingleOrDefault();
		}

		private readonly IDictionary<string, string> callSiteToLocalVariable = new Dictionary<string, string>();
	}

	internal class NameExtractorVisitor : SyntaxVisitor<string>
	{
		private static Lazy<NameExtractorVisitor> instance = new Lazy<NameExtractorVisitor>( () => new NameExtractorVisitor() );

		public static NameExtractorVisitor Instance
		{
			get { return instance.Value; }
		}

		public override string VisitLiteralExpression(LiteralExpressionSyntax node)
		{
			return Normalize(node.Token.ToString());
		}

		public override string VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			return node.Expression.Accept(this);
		}

		public override string VisitIdentifierName(IdentifierNameSyntax node)
		{
			return Normalize(node.Identifier.ToString());
		}

		private static string Normalize(string value)
		{
			return Regex.Replace(value, @"\.", "_");
		}
	}

	internal class ValueTypeOnCallSiteFixer : SyntaxVisitor<MemberAccessExpressionSyntax>
	{
		private readonly MemberAccessExpressionSyntax parent;
		private readonly IDictionary<string, string> callSiteToLocalVariable;

		public ValueTypeOnCallSiteFixer(MemberAccessExpressionSyntax parent, IDictionary<string, string> callSiteToLocalVariable)
		{
			this.parent = parent;
			this.callSiteToLocalVariable = callSiteToLocalVariable;
		}

		public override MemberAccessExpressionSyntax VisitIdentifierName(IdentifierNameSyntax node)
		{
			return ReplaceExpressionOnMemberAccessExpression(node);
		}

		public override MemberAccessExpressionSyntax VisitLiteralExpression(LiteralExpressionSyntax node)
		{
			return ReplaceExpressionOnMemberAccessExpression(node);
		}

		public override MemberAccessExpressionSyntax VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			return ReplaceExpressionOnMemberAccessExpression(node);
		}

		private MemberAccessExpressionSyntax ReplaceExpressionOnMemberAccessExpression(ExpressionSyntax node)
		{
			var localVarName = callSiteToLocalVariable[node.ToString()];
			var localVarIdentifier = Syntax.Identifier(localVarName)
			                               .WithLeadingTrivia(node.GetLeadingTrivia())
			                               .WithTrailingTrivia(node.GetTrailingTrivia());

			return Syntax.MemberAccessExpression(SyntaxKind.MemberAccessExpression, Syntax.IdentifierName(localVarIdentifier), parent.Name);
		}
	}

	internal class MemberAccessOnValueTypeCollectorVisitor : SyntaxWalker
	{
		public static IList<ExpressionSyntax> Collect(SyntaxNode block)
		{
			var collected = new List<ExpressionSyntax>();
			block.Accept(new MemberAccessOnValueTypeCollectorVisitor(collected));

			return collected;
		}

		public override void VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			AddNodeIfRequired(node);
			base.VisitInvocationExpression(node);
		}

		public override void VisitLiteralExpression(LiteralExpressionSyntax node)
		{
			AddNodeIfRequired(node);
			base.VisitLiteralExpression(node);
		}

		public override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			var ie = node.Parent as InvocationExpressionSyntax;
			if (ie != null && ie.Expression == node)
			{
				collected.Add(node);
			}

			base.VisitIdentifierName(node);
		}

		private void AddNodeIfRequired(ExpressionSyntax node)
		{
			var mae = node.Parent as MemberAccessExpressionSyntax;
			if (mae != null && mae.Expression == node && mae.Parent.Kind == SyntaxKind.InvocationExpression)
			{
				collected.Add(node);
			}
		}

		private MemberAccessOnValueTypeCollectorVisitor(IList<ExpressionSyntax> collected)
		{
			this.collected = collected;
		}

		private readonly IList<ExpressionSyntax> collected;
	
	}
}