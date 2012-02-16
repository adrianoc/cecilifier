using System;
using System.IO;
using System.Linq;
using Ceciifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Core.Extensions;
using Mono.Cecil;
using NUnit.Framework;

namespace Ceciifier.Core.Tests.Framework
{
	public class ResourceTestBase
	{
		protected void AssertResourceTest(string resourceName)
		{
			var tbc = ReadResource(resourceName, "cs");
			var expected = ReadResource(resourceName, "cecil");

			var expectedCecilDriver = new StreamReader(expected).ReadToEnd().AsCecilApplication();
			Assert.AreEqual(expectedCecilDriver, Cecilfy(tbc));

			var compiledCecilifierPath = CompilationServices.CompileExe(expectedCecilDriver, typeof(TypeDefinition).Assembly, typeof(IQueryable).Assembly);

			var actualAssemblyPath = Path.Combine(Path.GetTempPath(), resourceName + ".dll");
			Directory.CreateDirectory(Path.GetDirectoryName(actualAssemblyPath));

			TestFramework.Execute(compiledCecilifierPath, actualAssemblyPath);
			var expectedAssemblyPath = CompilationServices.CompileDLL(ReadToEnd(tbc));

			Console.WriteLine("Cecil build assembly path: {0}", actualAssemblyPath);
			Console.WriteLine("Cecil runner path: {0}", compiledCecilifierPath);
			Console.WriteLine("Compiled from res: {0}", expectedAssemblyPath);
			
			CompareAssemblies(expectedAssemblyPath, actualAssemblyPath);
		}

		private static string ReadToEnd(Stream tbc)
		{
			tbc.Seek(0, SeekOrigin.Begin);
			return new StreamReader(tbc).ReadToEnd();
		}

		private void CompareAssemblies(string expectedAssemblyPath, string actualAssemblyPath)
		{
			var comparer = new AssemblyComparer(expectedAssemblyPath, actualAssemblyPath);
			var visitor = new StrictAssemblyDiffVisitor();
			if (!comparer.Compare(visitor))
			{
				Assert.Fail(string.Format("Expected and generated assemblies differs:\r\n\tExpected:  {0}\r\n\tGenerated: {1}\r\n\r\n{2}", comparer.First, comparer.Second, visitor.Reason));
			}
		}

		private Stream ReadResource(string resourceName, string type)
		{
			return new FileStream(Path.Combine("TestResources", resourceName + "." + type + ".txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
		}

		private string Cecilfy(Stream stream)
		{
			return Cecilifier.Core.Cecilifier.Process(stream).ReadToEnd();
		}
	}
}
