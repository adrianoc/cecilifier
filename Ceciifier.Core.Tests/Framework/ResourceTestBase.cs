using System;
using System.IO;
using System.Linq;
using Ceciifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Runtime;
using Mono.Cecil;
using NUnit.Framework;

namespace Ceciifier.Core.Tests.Framework
{
	public class ResourceTestBase
	{
		protected void AssertResourceTestBinary(string resourceBasePath)
		{
			string expectedAssemblyPath = resourceBasePath.GetPathOfBinaryResource("Expected.dll");
			
			var tbc = ReadResource(resourceBasePath, "cs");
			AssertResourceTest(resourceBasePath, expectedAssemblyPath, tbc);
		}

		protected void AssertResourceTest(string resourceName)
		{
			var tbc = ReadResource(resourceName, "cs");

			var actualAssemblyPath = Path.Combine(Path.GetTempPath(), resourceName + ".dll");

			var expectedAssemblyPath = CompilationServices.CompileDLL(
												Path.Combine(Path.GetDirectoryName(actualAssemblyPath), Path.GetFileNameWithoutExtension(actualAssemblyPath) + "_expected"),
												ReadToEnd(tbc));

			AssertResourceTest(resourceName, expectedAssemblyPath, tbc);
		}

		private void AssertResourceTest(string resourceBasePath, string expectedAssemblyPath, Stream tbc)
		{
			var actualAssemblyPath = Path.Combine(Path.GetTempPath(), resourceBasePath + ".dll");

			var generated = Cecilfy(tbc);

			var compiledCecilifierPath = CompilationServices.CompileExe(generated, typeof(TypeDefinition).Assembly,
																		typeof(IQueryable).Assembly, typeof(TypeHelpers).Assembly);

			Directory.CreateDirectory(Path.GetDirectoryName(actualAssemblyPath));

			try
			{
				TestFramework.Execute(compiledCecilifierPath, actualAssemblyPath);
			}
			catch (Exception ex)
			{
				Assert.Fail("Fail to execute generated cecil snipet: " + ex + "\r\n" + generated);
			}

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
				Assert.Fail("Expected and generated assemblies differs:\r\n\tExpected:  {0}\r\n\tGenerated: {1}\r\n\r\n{2}", comparer.First, comparer.Second, visitor.Reason);
			}
		}

		private Stream ReadResource(string resourceName, string type)
		{
			return ReadResource(resourceName.GetPathOfTextResource(type));
		}

		private Stream ReadResource(string path)
		{
			return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		}

		private string Cecilfy(Stream stream)
		{
			stream.Position = 0;
			return Cecilifier.Core.Cecilifier.Process(stream).ReadToEnd();
		}
	}
}
