using System.IO;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core
{
	public sealed class Cecilifier
	{
		public static StringReader Process(Stream content)
		{
			var syntaxTree = SyntaxTree.ParseCompilationUnit(new StreamReader(content).ReadToEnd());

			//TODO: Get the list of referenced assemblies as an argument
			var comp = Compilation.Create("Teste").AddReferences(new AssemblyFileReference(typeof(object).Assembly.Location)).AddSyntaxTrees(syntaxTree);
			var semanticModel = comp.GetSemanticModel(syntaxTree);
		
			var visitor = new CecilifierVisitor(semanticModel);
			visitor.Visit(syntaxTree.Root);

			return new StringReader(visitor.Output.AsCecilApplication());
		}
	}
}
