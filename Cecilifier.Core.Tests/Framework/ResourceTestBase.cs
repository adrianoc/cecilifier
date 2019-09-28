using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Runtime;
using Mono.Cecil;
using Mono.Cecil.Rocks;
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
            var expectedAssemblyPath = resourceBasePath.GetPathOfBinaryResource("Expected.dll", kind);

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
            AssertResourceTest(resourceName, kind, new StrictAssemblyDiffVisitor());
        }
        
        internal void AssertResourceTest(string resourceName, TestKind kind, IAssemblyDiffVisitor visitor)
        {
            using (var tbc = ReadResource(resourceName, "cs", kind))
            {
                var cecilifierTestsFolder = Path.Combine(Path.GetTempPath(), "CecilifierTests");

                var cecilifiedAssemblyPath = Path.Combine(cecilifierTestsFolder, resourceName + ".dll");
                var resourceCompiledAssemblyPath = CompilationServices.CompileDLL(
                    Path.Combine(Path.GetDirectoryName(cecilifiedAssemblyPath), Path.GetFileNameWithoutExtension(cecilifiedAssemblyPath) + "_expected"),
                    ReadToEnd(tbc),
                    Utils.GetTrustedAssembliesPath().ToArray());

                Console.WriteLine();
                Console.WriteLine("Compiled from res        : {0}", resourceCompiledAssemblyPath);
                Console.WriteLine("Generated from Cecilifier: {0}", cecilifiedAssemblyPath);

                AssertResourceTest(cecilifiedAssemblyPath, resourceCompiledAssemblyPath, tbc, visitor);
            }
        }

        private void AssertResourceTest(string actualAssemblyPath, string expectedAssemblyPath, Stream tbc, IAssemblyDiffVisitor visitor)
        {
            CecilifyAndExecute(tbc, actualAssemblyPath);
            CompareAssemblies(expectedAssemblyPath, actualAssemblyPath, visitor);
        }
        
        private void AssertResourceTest(string actualAssemblyPath, string expectedAssemblyPath, Stream tbc)
        {
            AssertResourceTest(actualAssemblyPath, expectedAssemblyPath, tbc, new StrictAssemblyDiffVisitor());
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

            var references = Utils.GetTrustedAssembliesPath();

            var refsToCopy = new List<string>
            {
                typeof(ILParser).Assembly.Location,
                typeof(TypeReference).Assembly.Location,
                typeof(TypeHelpers).Assembly.Location
            };

            foreach (var refPath in refsToCopy)
            {
                references.Add(refPath);
            }

            var cecilifierRunnerPath = CompilationServices.CompileExe(cecilifiedCode, references.ToArray());

            Console.WriteLine("------- Cecilified Code -------");
            Console.WriteLine(cecilifiedCode);
            Console.WriteLine("^^^^^^^ Cecilified Code ^^^^^^^");

            Directory.CreateDirectory(Path.GetDirectoryName(outputAssembyPath));
            CopyFilesNextToGeneratedExecutable(cecilifierRunnerPath, refsToCopy);
            Console.WriteLine("Cecil runner path: {0}", cecilifierRunnerPath);

            TestFramework.Execute("dotnet", cecilifierRunnerPath + " " + outputAssembyPath);
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

        private void CompareAssemblies(string expectedAssemblyPath, string actualAssemblyPath, IAssemblyDiffVisitor visitor)
        {
            var comparer = new AssemblyComparer(expectedAssemblyPath, actualAssemblyPath);
            if (!comparer.Compare(visitor))
            {
                Assert.Fail("Expected and generated assemblies differs:\r\n\tExpected:  {0}\r\n\tGenerated: {1}\r\n\r\n{2}\r\n\r\nCecilified Code:\r\n{3}", comparer.First, comparer.Second, visitor.Reason, cecilifiedCode);
            }
        }
        
        private void CompareAssemblies(string expectedAssemblyPath, string actualAssemblyPath)
        {
            CompareAssemblies(expectedAssemblyPath, actualAssemblyPath, new StrictAssemblyDiffVisitor());
        }

        private Stream ReadResource(string resourceName, string type, TestKind kind)
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
            return Cecilifier.Process(stream, Utils.GetTrustedAssembliesPath()).ReadToEnd();
        }
    }
}
