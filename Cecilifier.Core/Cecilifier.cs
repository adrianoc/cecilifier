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
			var syntaxTree = SyntaxTree.ParseCompilationUnit(new StreamReader(content).ReadToEnd());

			//TODO: Get the list of referenced assemblies as an argument
			var comp = Compilation.Create(
                                  "Teste", 
                                  new CompilationOptions(assemblyKind:AssemblyKind.DynamicallyLinkedLibrary),
		                          new[] { syntaxTree },
                                  new[] { MetadataReference.Create(typeof(object).Assembly.Location) });

            foreach (var diag in comp.GetDiagnostics())
            {
                Console.WriteLine(diag.Info.GetMessage());
            }
            
            var semanticModel = comp.GetSemanticModel(syntaxTree);
		
			IVisitorContext ctx = new CecilifierContext(semanticModel);
			var visitor = new CompilationUnitVisitor(ctx);
			visitor.Visit(syntaxTree.Root);

			return new StringReader(ctx.Output.AsCecilApplication());
		}
	}
}




