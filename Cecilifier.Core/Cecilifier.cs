using System;
using System.IO;
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
			CompilationUnitSyntax root;
			tree.TryGetRoot(out root);
			var cu = (CompilationUnitSyntax) root.Accept(new LiteralToLocalVariableVisitor());

			return SyntaxTree.Create(cu);
		}
	}
}