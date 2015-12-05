using System;
using System.IO;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
			var syntaxTree = CSharpSyntaxTree.ParseText(new StreamReader(content).ReadToEnd());

			//TODO: Get the list of referenced assemblies as an argument
			var comp = CSharpCompilation.Create(
							"Teste",
							new[] { syntaxTree },
							new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
							new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

			foreach (var diag in comp.GetDiagnostics())
			{
				Console.WriteLine(diag.GetMessage());
			}

			var semanticModel = comp.GetSemanticModel(syntaxTree);

			//TODO: What exactly are we transforming ?
			//syntaxTree = RunTransformations(syntaxTree, semanticModel);

			var comp2 = CSharpCompilation.Create(
							"Test",
							new[] { syntaxTree },
							new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
							new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

			IVisitorContext ctx = new CecilifierContext(comp2.GetSemanticModel(syntaxTree));
			var visitor = new CompilationUnitVisitor(ctx);

			SyntaxNode root;
			syntaxTree.TryGetRoot(out root);
			visitor.Visit(root);

			//new SyntaxTreeDump("TREE: ", root);

			return new StringReader(ctx.Output.AsCecilApplication());
		}

		private SyntaxTree RunTransformations(SyntaxTree tree, SemanticModel semanticModel)
		{
			SyntaxNode root;
			tree.TryGetRoot(out root);

			var cu = (CompilationUnitSyntax) ((CompilationUnitSyntax) root).Accept(new ValueTypeToLocalVariableVisitor(semanticModel));

			return CSharpSyntaxTree.Create(cu);
		}
	}
}