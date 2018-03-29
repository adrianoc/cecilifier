using System;
using System.IO;
using System.Linq;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Runtime;
using Mono.Cecil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Framework
{
	public class ResourceTestBase
	{
	    [SetUp]
	    public void Setup()
	    {
            Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
	    }

		protected void AssertResourceTestBinary(string resourceBasePath, TestKind kind)
		{
			string expectedAssemblyPath = resourceBasePath.GetPathOfBinaryResource("Expected.dll", kind);
			
			var tbc = ReadResource(resourceBasePath, "cs", kind);
			AssertResourceTest(resourceBasePath, expectedAssemblyPath, tbc);
		}

		protected void AssertResourceTest(string resourceName, TestKind kind)
		{
			var tbc = ReadResource(resourceName, "cs", kind);

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

				CompareAssemblies(expectedAssemblyPath, actualAssemblyPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Cecil build assembly path: {0}", actualAssemblyPath);
				Console.WriteLine("Cecil runner path: {0}", compiledCecilifierPath);
				Console.WriteLine("Compiled from res: {0}", expectedAssemblyPath);

				Console.WriteLine("Fail to execute generated cecil snipet: {0}\r\n{1}", ex, generated);

				throw;
			}
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

		protected Stream ReadResource(string resourceName, string type, TestKind kind)
		{
			return ReadResource(resourceName.GetPathOfTextResource(type, kind));
		}

		private Stream ReadResource(string path)
		{
			return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		}

		private string Cecilfy(Stream stream)
		{
			stream.Position = 0;
			return Cecilifier.Process(stream).ReadToEnd();
		}
	}
}
