using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		public static StringReader Process(Stream content, IList<string> references)
		{
			var cecilifier = new Cecilifier();
			return cecilifier.Run(content, references);
		}

		private StringReader Run(Stream content, IList<string> references)
		{
			var syntaxTree = CSharpSyntaxTree.ParseText(new StreamReader(content).ReadToEnd());

			var comp = CSharpCompilation.Create(
							"CecilifiedAssembly",
							new[] { syntaxTree },
							references.Select(refPath => MetadataReference.CreateFromFile(refPath)),
							new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe:true));

			foreach (var diag in comp.GetDiagnostics())
			{
				Console.WriteLine(diag.GetMessage());
			}

			if (comp.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
			{
				Console.WriteLine("Code contains compiler errors (see above) and will not be cecilified.");
				return new StringReader(string.Empty);
			}

			var semanticModel = comp.GetSemanticModel(syntaxTree);

			var ctx = new CecilifierContext(semanticModel);
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