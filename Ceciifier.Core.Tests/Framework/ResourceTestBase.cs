using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
			var generated = Cecilfy(tbc);

			AssertCecilified(expectedCecilDriver, generated);

			var compiledCecilifierPath = CompilationServices.CompileExe(generated, typeof(TypeDefinition).Assembly, typeof(IQueryable).Assembly);

			var actualAssemblyPath = Path.Combine(Path.GetTempPath(), resourceName + ".dll");
			Directory.CreateDirectory(Path.GetDirectoryName(actualAssemblyPath));

			try
			{
				TestFramework.Execute(compiledCecilifierPath, actualAssemblyPath);
			}
			catch(Exception ex)
			{
				Assert.Fail("Fail to execute generated cecil snipet: " + ex + "\r\n" + generated);
			}

			var expectedAssemblyPath = CompilationServices.CompileDLL(ReadToEnd(tbc));

			Console.WriteLine("Cecil build assembly path: {0}", actualAssemblyPath);
			Console.WriteLine("Cecil runner path: {0}", compiledCecilifierPath);
			Console.WriteLine("Compiled from res: {0}", expectedAssemblyPath);
			
			CompareAssemblies(expectedAssemblyPath, actualAssemblyPath);
		}

		private static void AssertCecilified(string expected, string actual)
		{
			var expectedReader = new StringReader(expected);
			var actualReader = new IgnoringStringReader(actual);

			string actualLine;
			while ( (actualLine = actualReader.ReadLine()) != null )
			{
				var expectedLine = expectedReader.ReadLine();
				if (expectedLine == null)
				{
					Assert.Fail("Expectation file is to short.");
				}

				var rawExpectation = expectedLine.Replace("\t", "").Trim();
				if (rawExpectation.StartsWith("!*")) return;

				var match = Regex.Match(rawExpectation, "!~(?<linesToSkip>[0-9])*.*");
				if (match.Success)
				{
					int linesToIgnore = Int32.Parse(match.Groups[1].Value) - 1;
					actualReader.IgnoreNextLines = linesToIgnore;
					continue;
				}

				if (rawExpectation == "!") continue;

				Assert.AreEqual(expectedLine, actualLine, expected + "\r\n-------------------\r\n"+actual);
			}

			Assert.IsNull(expectedReader.ReadLine());
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
