using System;
using System.IO;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core
{
	public sealed class Cecilifier
	{
		public static StringReader Process(Stream content)
		{
			var cecilifier = new Cecilifier();
			return cecilifier.Run(content);
		}

		private StringReader Run(Stream content)
		{
			var syntaxTree = SyntaxTree.ParseText(new StreamReader(content).ReadToEnd());

			syntaxTree = RunTransformations(syntaxTree);

			//TODO: Get the list of referenced assemblies as an argument
			var comp = Compilation.Create(
				"Teste",
				new CompilationOptions(OutputKind.DynamicallyLinkedLibrary),
				new[] { syntaxTree },
				new[] { MetadataReference.CreateAssemblyReference(typeof (object).Assembly.FullName) });

			foreach (var diag in comp.GetDiagnostics())
			{
				Console.WriteLine(diag.Info.GetMessage());
			}

			var semanticModel = comp.GetSemanticModel(syntaxTree);

			IVisitorContext ctx = new CecilifierContext(semanticModel);
			var visitor = new CompilationUnitVisitor(ctx);

			CompilationUnitSyntax root;
			syntaxTree.TryGetRoot(out root);
			visitor.Visit(root);

			new SyntaxTreeDump("TREE: ", root);

			return new StringReader(ctx.Output.AsCecilApplication());
		}

		private SyntaxTree RunTransformations(SyntaxTree tree)
		{
			return tree;
			//CompilationUnitSyntax root;
			//tree.TryGetRoot(out root);

			//return SyntaxTree.Create((CompilationUnitSyntax) root.Accept(new LiteralToLocalVariableVisitor()));
		}
	}

	internal class LiteralToLocalVariableVisitor : SyntaxRewriter
	{
		public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			if (node.Expression.Kind == SyntaxKind.NumericLiteralExpression)
			{
				var old = (LiteralExpressionSyntax)node.Expression;
				var jIdentifier = Syntax.Identifier("m")
											.WithLeadingTrivia(old.GetLeadingTrivia())
											.WithTrailingTrivia(old.GetTrailingTrivia());

				return Syntax.MemberAccessExpression(SyntaxKind.MemberAccessExpression, Syntax.IdentifierName(jIdentifier), node.Name);

			}

			return base.VisitMemberAccessExpression(node);
		}

		public override SyntaxNode VisitBlock(BlockSyntax node)
		{
			var typeSyntax = Syntax.ParseTypeName("int").WithLeadingTrivia(node.ChildNodes().First().GetLeadingTrivia());
			var m = Syntax.VariableDeclarator("m").WithLeadingTrivia(Syntax.Space);
			var constM = Syntax.VariableDeclaration(typeSyntax, Syntax.SeparatedList(m));

			var withNewLocal = node.WithStatements(Syntax.List(new[] 
			{
				Syntax.LocalDeclarationStatement(constM)
			}.Concat(node.Statements)));

			return base.VisitBlock(withNewLocal);
		}
	}
}




