using System;
using System.IO;
using System.Text;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Framework
{
    public class ResourceTestBase : CecilifierTestBase
    {
        protected void AssertResourceTest(string resourceName)
        {
            AssertResourceTest(resourceName, new ResourceTestOptions() { ResourceName = resourceName, ToBeCecilified = ReadResource(resourceName, "cs") });
        }

        protected void AssertResourceTest(ResourceTestOptions options)
        {
            options.ToBeCecilified ??= ReadResource(options.ResourceName, "cs");
            AssertResourceTest(options.ResourceName, options);
        }

        protected void AssertResourceTestWithExplicitExpectation(string resourceName, string methodSignature)
        {
            var options = new ResourceTestOptions() { ResourceName = resourceName, FailOnAssemblyVerificationErrors = true, BuildType = BuildType.Dll };
            AssertResourceTestWithExplicitExpectation(options, methodSignature);
        }
        
        protected void AssertResourceTestWithExplicitExpectation(ResourceTestOptions options, string methodSignature)
        {
            options.ToBeCecilified ??= ReadResource(options.ResourceName, "cs");
            using var expectedILStream = ReadResource(options.ResourceName, "cs.il");
            var expectedIL = ReadToEnd(expectedILStream);

            var testBaseOutputPath = Path.Combine(GetTestOutputBaseFolderFor("Integration"), options.ResourceName);
            var cecilifyResult = AssertResourceTestWithExplicitExpectedIL(testBaseOutputPath, expectedIL, methodSignature, options);

            Console.WriteLine();
            Console.WriteLine($"Expected IL:{expectedIL}\n");
            Console.WriteLine($"Actual assembly path : {cecilifyResult.CecilifiedOutputAssemblyFilePath}");
        }

        protected void AssertResourceTestWithParameters(string resourceName, params string[] parameters)
        {
            using var tbc = ReadResource(resourceName, "cs");
            var readToEnd = ReadToEnd(tbc);

            var testContents = string.Format(readToEnd, parameters);
            var options = new ResourceTestOptions()
            {
                ResourceName = $"{resourceName}_{string.Join('_', parameters)}",
                AssemblyComparison = new StrictAssemblyDiffVisitor(),
                BuildType = BuildType.Dll,
                ToBeCecilified = new MemoryStream(Encoding.ASCII.GetBytes(testContents))
            };

            AssertResourceTest(options);
        }

        protected void AssertResourceTestBinary(string resourceBasePath)
        {
            var expectedAssemblyPath = resourceBasePath.GetPathOfBinaryResource("Expected.dll");

            var tbc = ReadResource(resourceBasePath, "cs");
            var testBasePath =GetTestOutputBaseFolderFor("Integration");
            AssertResourceTest(testBasePath, expectedAssemblyPath, tbc);
        }

        protected void AssertResourceTest(string resourceName, ResourceTestOptions options)
        {
            var testsFolder = Path.Combine(GetTestOutputBaseFolderFor("Integration"), resourceName);

            var cecilifiedAssemblyPath = Path.Combine(testsFolder, Path.GetFileName(resourceName) + ".dll");
            var resourceCompiledAssemblyPath = CompileExpectedTestAssembly(cecilifiedAssemblyPath, options.BuildType, ReadToEnd(options.ToBeCecilified));

            Console.WriteLine();
            Console.WriteLine("Compiled from res        : {0}", resourceCompiledAssemblyPath);
            Console.WriteLine("Generated from Cecilifier: {0}", cecilifiedAssemblyPath);

            AssertResourceTest(testsFolder, resourceCompiledAssemblyPath, options);
        }

        private void AssertResourceTest(string testBasePath, string expectedAssemblyPath, ResourceTestOptions options)
        {
            var cecilifyResult = CecilifyAndExecute(options.ToBeCecilified, testBasePath);
            CompareAssemblies(expectedAssemblyPath, cecilifyResult.CecilifiedOutputAssemblyFilePath, options.AssemblyComparison, options.InstructionComparer);
            VerifyAssembly(cecilifyResult.CecilifiedOutputAssemblyFilePath, expectedAssemblyPath, options);
        }

        private void AssertResourceTest(string testBasePath, string expectedAssemblyPath, Stream tbc)
        {
            AssertResourceTest(testBasePath, expectedAssemblyPath, new ResourceTestOptions { ToBeCecilified = tbc, AssemblyComparison = new StrictAssemblyDiffVisitor() });
        }

        private CecilifyResult AssertResourceTestWithExplicitExpectedIL(string testOutputBasePath, string expectedIL, string methodSignature, ResourceTestOptions options)
        {
            var cecilifyResult = CecilifyAndExecute(options.ToBeCecilified, testOutputBasePath);

            VerifyAssembly(cecilifyResult.CecilifiedOutputAssemblyFilePath, null, options);

            var actualIL = GetILFrom(cecilifyResult.CecilifiedOutputAssemblyFilePath, methodSignature);
            Assert.That(actualIL, Is.EqualTo(expectedIL), $"Actual IL differs from expected.\nActual Assembly Path = {cecilifyResult.CecilifiedOutputAssemblyFilePath}\nExpected IL:\n{expectedIL}\nActual IL:{actualIL}");

            return cecilifyResult;
        }

        protected Stream ReadResource(string resourceName, string type) => ReadResource(resourceName.GetPathOfTextResource(type));
        private Stream ReadResource(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
