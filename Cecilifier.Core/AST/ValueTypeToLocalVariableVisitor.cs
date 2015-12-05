using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
	internal class ValueTypeToLocalVariableVisitor : CSharpSyntaxRewriter
	{
		public ValueTypeToLocalVariableVisitor(SemanticModel semanticModel)
		{
			this.semanticModel = semanticModel;
		}

		public override SyntaxNode VisitBlock(BlockSyntax block)
		{
            var callSitesToFix = InvocationsOnValueTypes(block);

			var memberName = EnclosingMethodName(block);
			if (memberName == null && callSitesToFix.Count > 0)
			{
				throw new NotSupportedException("Expansion of literals to locals outside methods not supported yet: " + block.ToFullString() + "  " + block.SyntaxTree.GetLineSpan(block.Span));
			}
			
			var transformedBlock = block;
			foreach (var callSite in callSitesToFix.Reverse())
			{
				transformedBlock = InsertLocalVariableStatementFor(callSite, memberName, transformedBlock);
			}

			return base.VisitBlock(transformedBlock);
		}

		public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			if (node.Parent.Kind() != SyntaxKind.InvocationExpression) return node;

			MemberAccessExpressionSyntax newMae = null;
			if (callSiteToLocalVariable.ContainsKey(node.Expression.ToString()))
				newMae = node.Expression.Accept(new ValueTypeOnCallSiteFixer(node, callSiteToLocalVariable));

			return newMae ?? base.VisitMemberAccessExpression(node);
		}

		private BlockSyntax InsertLocalVariableStatementFor(ExpressionSyntax callSite, string methodName, BlockSyntax block)
		{
			var typeSyntax = VarTypeSyntax(block);
			var lineSpan = callSite.SyntaxTree.GetLineSpan(callSite.Span);

			var varDecl = SyntaxFactory.VariableDeclaration(
								typeSyntax, 
								SyntaxFactory.SeparatedList(new[] { VariableDeclaratorFor(callSite, string.Format("{0}_{1}", lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character), methodName) }));

			var newBlockBody = InsertLocalVariableDeclarationBeforeCallSite(block, varDecl, callSite);

			return block.WithStatements(newBlockBody);
		}

		private static TypeSyntax VarTypeSyntax(BlockSyntax block)
		{
			return SyntaxFactory.ParseTypeName("var").WithLeadingTrivia(block.ChildNodes().First().GetLeadingTrivia());
		}

		private static SyntaxList<StatementSyntax> InsertLocalVariableDeclarationBeforeCallSite(BlockSyntax block, VariableDeclarationSyntax varDecl, ExpressionSyntax callSite)
		{
			var stmts = block.Statements;

			var callSiteStatement = callSite.Ancestors().First(node => node.Parent.Kind() == SyntaxKind.Block);

			var i = stmts.IndexOf(stmt => stmt.IsEquivalentTo(callSiteStatement));
			
			var newStmts = stmts.Insert(i, SyntaxFactory.LocalDeclarationStatement(varDecl).WithNewLine());

			return SyntaxFactory.List(newStmts.AsEnumerable());
		}

		private VariableDeclaratorSyntax VariableDeclaratorFor(ExpressionSyntax callSite, string typeName, string context)
		{
			var localVariableName = LocalVarNameFor(callSite, typeName, context);
			callSiteToLocalVariable[callSite.ToString()] = localVariableName;

			var declarator = SyntaxFactory.VariableDeclarator(localVariableName).WithLeadingTrivia(SyntaxFactory.Space);
			return declarator.WithInitializer(SyntaxFactory.EqualsValueClause(InitializeTrivia(callSite)).WithLeadingTrivia(SyntaxFactory.Space));
		}

		private static T InitializeTrivia<T>(T callSite) where T : SyntaxNode
		{
			return callSite.ReplaceTrivia(callSite.GetLeadingTrivia().AsEnumerable(), (trivia, syntaxTrivia) => SyntaxFactory.Space);
		}

		private IList<ExpressionSyntax> InvocationsOnValueTypes(BlockSyntax block)
		{
			return MemberAccessOnValueTypeCollectorVisitor.Collect(semanticModel, block);
		}

		private static string LocalVarNameFor(ExpressionSyntax callSite, string typeName, string context)
		{
			return string.Format("{0}_{1}_{2}", context, typeName, callSite.Accept(NameExtractorVisitor.Instance));
		}

		private static string EnclosingMethodName(BlockSyntax block)
		{
			var declaringMethod = block.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
			if (declaringMethod != null)
				return declaringMethod.Identifier.ValueText;

			var declaringCtor = block.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
			if (declaringCtor != null)
				return declaringCtor.Identifier.ValueText;

			return null;
		}

		private readonly IDictionary<string, string> callSiteToLocalVariable = new Dictionary<string, string>();
		private readonly SemanticModel semanticModel;
	}

	internal class NameExtractorVisitor : CSharpSyntaxVisitor<string>
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

	internal class ValueTypeOnCallSiteFixer : CSharpSyntaxVisitor<MemberAccessExpressionSyntax>
	{
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

		private MemberAccessExpressionSyntax ReplaceExpressionOnMemberAccessExpression(SyntaxNode node)
		{
			var localVarName = callSiteToLocalVariable[node.ToString()];
			var localVarIdentifier = SyntaxFactory.Identifier(localVarName)
			                               .WithLeadingTrivia(node.GetLeadingTrivia())
			                               .WithTrailingTrivia(node.GetTrailingTrivia());

			return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(localVarIdentifier), parent.Name);
		}

		private readonly MemberAccessExpressionSyntax parent;
		private readonly IDictionary<string, string> callSiteToLocalVariable;
	}

	internal class MemberAccessOnValueTypeCollectorVisitor : CSharpSyntaxWalker
	{
		public static IList<ExpressionSyntax> Collect(SemanticModel semanticModel, BlockSyntax block)
		{
			var collected = new List<ExpressionSyntax>();
			block.Accept(new MemberAccessOnValueTypeCollectorVisitor(semanticModel, collected));

			return collected;
		}

		public override void VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			var mae = node.Expression as MemberAccessExpressionSyntax;
			if (mae != null)
			{
				var s= semanticModel.GetSymbolInfo(mae.Expression);
				if (s.Symbol != null && (s.Symbol.Kind == SymbolKind.Field || s.Symbol.Kind == SymbolKind.Parameter || s.Symbol.Kind == SymbolKind.Local))
					return;

				var ti = semanticModel.GetTypeInfo(mae.Expression);
				if (ti.Type.IsValueType)
					collected.Add(mae.Expression);
			}
			base.VisitInvocationExpression(node);
		}

		private MemberAccessOnValueTypeCollectorVisitor(SemanticModel semanticModel, IList<ExpressionSyntax> collected)
		{
			this.semanticModel = semanticModel;
			this.collected = collected;
		}

		private readonly SemanticModel semanticModel;
		private readonly IList<ExpressionSyntax> collected;
	}
}