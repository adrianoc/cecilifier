using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Runtime;
using Microsoft.CodeAnalysis;
using Mono.Cecil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Framework
{
	public class ResourceTestBase
	{
		private string cecilifiedCode;
		
	    [SetUp]
	    public void Setup()
	    {
		    cecilifiedCode = string.Empty;
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
			    var cecilifierTestsFolder = Path.Combine(Path.GetTempPath(), "CecilifierTests");
			    
		        var actualAssemblyPath = Path.Combine(cecilifierTestsFolder, resourceName + ".dll");
		        var resourceCompiledAssemblyPath = CompilationServices.CompileDLL(
		                                                Path.Combine(Path.GetDirectoryName(actualAssemblyPath), Path.GetFileNameWithoutExtension(actualAssemblyPath) + "_expected"),
		                                                ReadToEnd(tbc),
														GetTrustedAssembliesPath().ToArray());

			    Console.WriteLine();
			    Console.WriteLine("Compiled from res        : {0}", resourceCompiledAssemblyPath);
			    Console.WriteLine("Generated from Cecilifier: {0}", actualAssemblyPath);

			    AssertResourceTest(actualAssemblyPath, resourceCompiledAssemblyPath, tbc);
		    }
		}

	    private void AssertResourceTest(string actualAssemblyPath, string expectedAssemblyPath, Stream tbc)
	    {
		    CecilifyAndExecute(tbc, actualAssemblyPath);
		    CompareAssemblies(expectedAssemblyPath, actualAssemblyPath);
	    }

		private void AssertResourceTestWithExplicitExpectedIL(string actualAssemblyPath, string expectedIL, string methodSignature, Stream tbc)
		{
			CecilifyAndExecute(tbc, actualAssemblyPath);

			var actualIL = GetILFrom(actualAssemblyPath, methodSignature);
			Assert.That(actualIL, Is.EqualTo(expectedIL), $"Actual IL differs from expected.\r\nActual Assembly Path = {actualAssemblyPath}\r\nActual IL:{actualIL}");
		}
		
		private void CecilifyAndExecute(Stream tbc, string outputAssembyPath)
		{
			cecilifiedCode = Cecilfy(tbc);

			var references = GetTrustedAssembliesPath();

			var refsToCopy = new List<string>
			{
				typeof(Mono.Cecil.Rocks.ILParser).Assembly.Location,
				typeof(TypeReference).Assembly.Location,
				typeof(TypeHelpers).Assembly.Location
			};
			
			foreach (var refPath in refsToCopy)
			{
				references.Add(refPath);
			}

			var cecilifierRunnerPath = CompilationServices.CompileExe(cecilifiedCode, references.ToArray());
			
			Directory.CreateDirectory(Path.GetDirectoryName(outputAssembyPath));
			try
			{
				CopyFilesNextToGeneratedExecutable(cecilifierRunnerPath, refsToCopy);
				
				TestFramework.Execute("dotnet", cecilifierRunnerPath + " " + outputAssembyPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Cecil runner path: {0}", cecilifierRunnerPath);
				Console.WriteLine("Fail to execute generated cecil snipet: {0}\r\n{1}", ex, cecilifiedCode);

				throw;
			}
		}

		private void CopyFilesNextToGeneratedExecutable(string cecilifierRunnerPath, List<string> refsToCopy)
		{
			var targetPath = Path.GetDirectoryName(cecilifierRunnerPath);
			foreach (var fileToCopy in refsToCopy)
			{
				File.Copy(fileToCopy, Path.Combine(targetPath, Path.GetFileName(fileToCopy)));
			}
			
			var sourceRuntimeConfigJson = Path.ChangeExtension(GetType().Assembly.Location, ".runtimeconfig.json"); 
			var targetRuntimeConfigJson = Path.ChangeExtension(cecilifierRunnerPath, ".runtimeconfig.json");
			
			File.Copy(sourceRuntimeConfigJson, targetRuntimeConfigJson);
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
				Assert.Fail("Expected and generated assemblies differs:\r\n\tExpected:  {0}\r\n\tGenerated: {1}\r\n\r\n{2}\r\n\r\nCecilified Code:\r\n{3}", comparer.First, comparer.Second, visitor.Reason, cecilifiedCode);
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

		private IList<string> GetTrustedAssembliesPath()
		{
			return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
		}
		
		private string Cecilfy(Stream stream)
		{
			stream.Position = 0;
			return Cecilifier.Process(stream, GetTrustedAssembliesPath()).ReadToEnd();
		}
	}
}
