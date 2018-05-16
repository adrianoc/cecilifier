using System;
using System.IO;
using System.Linq;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Runtime;
using Mono.Cecil;
using NUnit.Framework;
using NUnit.Framework.Constraints;

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
		    var actualAssemblyPath = Path.Combine(Path.GetTempPath(), "CecilifierTests/", resourceBasePath + ".dll");
			AssertResourceTest(actualAssemblyPath, expectedAssemblyPath, tbc);
		}

	    protected void AssertResourceTestWithExplictExpectation(string resourceName, string methodSignature)
	    {
	        using (var tbc = ReadResource(resourceName, "cs", TestKind.Integration))
	        using (var expectedILStream = ReadResource(resourceName, "cs.il", TestKind.Integration))
	        {
	            var expectedIL = ReadToEnd(expectedILStream);

	            var actualAssemblyPath = Path.Combine(Path.GetTempPath(), "CecilifierTests/", resourceName + ".dll");

	            AssertResourceTestWithExplicitExpectedIL(actualAssemblyPath, expectedIL, methodSignature, tbc);

	            Console.WriteLine();
	            Console.WriteLine("Expected IL: {0}", expectedIL);
	            Console.WriteLine("Actual assembly path : {0}", actualAssemblyPath);
	        }
	    }

		protected void AssertResourceTest(string resourceName, TestKind kind)
		{
		    using (var tbc = ReadResource(resourceName, "cs", kind))
		    {
		        var actualAssemblyPath = Path.Combine(Path.GetTempPath(), "CecilifierTests/", resourceName + ".dll");

		        var expectedAssemblyPath = CompilationServices.CompileDLL(
		                                                Path.Combine(Path.GetDirectoryName(actualAssemblyPath), 
		                                                Path.GetFileNameWithoutExtension(actualAssemblyPath) + "_expected"),
		                                                ReadToEnd(tbc));

		        AssertResourceTest(actualAssemblyPath, expectedAssemblyPath, tbc);

		        Console.WriteLine();
		        Console.WriteLine("Expected assembly path : {0}", expectedAssemblyPath);
		        Console.WriteLine("Actual   assembly path : {0}", actualAssemblyPath);
		    }
		}

	    private void AssertResourceTest(string actualAssemblyPath, string expectedAssemblyPath, Stream tbc)
		{
			var generated = Cecilfy(tbc);

			var compiledCecilifierPath = CompilationServices.CompileExe(generated, typeof(TypeDefinition).Assembly, typeof(Mono.Cecil.Rocks.ILParser).Assembly, typeof(IQueryable).Assembly, typeof(TypeHelpers).Assembly);

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

	    private void AssertResourceTestWithExplicitExpectedIL(string actualAssemblyPath, string expectedIL, string methodSignature, Stream tbc)
	    {
	        var generated = Cecilfy(tbc);

	        var compiledCecilifierPath = CompilationServices.CompileExe(generated, typeof(TypeDefinition).Assembly, typeof(Mono.Cecil.Rocks.ILParser).Assembly, typeof(IQueryable).Assembly, typeof(TypeHelpers).Assembly);

	        Directory.CreateDirectory(Path.GetDirectoryName(actualAssemblyPath));

	        try
	        {
	            TestFramework.Execute(compiledCecilifierPath, actualAssemblyPath);
	        }
	        catch (Exception ex)
	        {
	            Console.WriteLine("Cecil build assembly path: {0}", actualAssemblyPath);
	            Console.WriteLine("Cecil runner path: {0}", compiledCecilifierPath);

	            Console.WriteLine("Fail to execute generated cecil snipet: {0}\r\n{1}", ex, generated);

	            throw;
	        }

	        var actualIL = GetILFrom(actualAssemblyPath, methodSignature);
	        Assert.That(actualIL, Is.EqualTo(expectedIL), $"Actual IL differs from expected.\r\nActual Assembly Path = {actualAssemblyPath}");
	    }

        private string GetILFrom(string actualAssemblyPath, string methodSignature)
	    {
	        using (var assembly = AssemblyDefinition.ReadAssembly(actualAssemblyPath))
	        {
                var method = assembly.MainModule.Types.SelectMany(t => t.Methods).SingleOrDefault(m => m.FullName == methodSignature);
	            if (method == null)
	            {
                    Assert.Fail($"Method {methodSignature} could not be found in {actualAssemblyPath}");
	            }

	            return Formatter.FormatMethodBody(method).Replace("\t", "");
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
