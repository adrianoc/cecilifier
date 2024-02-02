using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Runtime;
using Mono.Cecil;
using Mono.Cecil.Cil;
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

            var actualAssemblyPath = Path.Combine(Path.GetTempPath(), "CecilifierTests/", options.ResourceName + ".dll");
            AssertResourceTestWithExplicitExpectedIL(actualAssemblyPath, expectedIL, methodSignature, options);

            Console.WriteLine();
            Console.WriteLine("Expected IL: {0}", expectedIL);
            Console.WriteLine("Actual assembly path : {0}", actualAssemblyPath);
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
            var actualAssemblyPath = Path.Combine(Path.GetTempPath(), "CecilifierTests/", resourceBasePath + ".dll");
            AssertResourceTest(actualAssemblyPath, expectedAssemblyPath, tbc);
        }

        protected void AssertResourceTest(string resourceName, ResourceTestOptions options)
        {
            var cecilifierTestsFolder = Path.Combine(Path.GetTempPath(), "CecilifierTests");

            var cecilifiedAssemblyPath = Path.Combine(cecilifierTestsFolder, resourceName + ".dll");
            var resourceCompiledAssemblyPath = CompileExpectedTestAssembly(cecilifiedAssemblyPath, options.BuildType, ReadToEnd(options.ToBeCecilified));

            Console.WriteLine();
            Console.WriteLine("Compiled from res        : {0}", resourceCompiledAssemblyPath);
            Console.WriteLine("Generated from Cecilifier: {0}", cecilifiedAssemblyPath);

            AssertResourceTest(cecilifiedAssemblyPath, resourceCompiledAssemblyPath, options);
        }

        private static string CompileExpectedTestAssembly(string cecilifiedAssemblyPath, BuildType buildType, string tbc)
        {
            var targetPath = Path.Combine(Path.GetDirectoryName(cecilifiedAssemblyPath), Path.GetFileNameWithoutExtension(cecilifiedAssemblyPath) + "_expected");

            return buildType == BuildType.Exe
                ? CompilationServices.CompileExe(targetPath, tbc, Utils.GetTrustedAssembliesPath().ToArray())
                : CompilationServices.CompileDLL(targetPath, tbc, Utils.GetTrustedAssembliesPath().ToArray());
        }

        private void AssertResourceTest(string actualAssemblyPath, string expectedAssemblyPath, ResourceTestOptions options)
        {
            CecilifyAndExecute(options.ToBeCecilified, actualAssemblyPath);
            CompareAssemblies(expectedAssemblyPath, actualAssemblyPath, options.AssemblyComparison, options.InstructionComparer);
            VerifyAssembly(actualAssemblyPath, expectedAssemblyPath, options);
        }

        private void AssertResourceTest(string actualAssemblyPath, string expectedAssemblyPath, Stream tbc)
        {
            AssertResourceTest(actualAssemblyPath, expectedAssemblyPath, new ResourceTestOptions { ToBeCecilified = tbc, AssemblyComparison = new StrictAssemblyDiffVisitor() });
        }

        private void AssertResourceTestWithExplicitExpectedIL(string actualAssemblyPath, string expectedIL, string methodSignature, ResourceTestOptions options)
        {
            CecilifyAndExecute(options.ToBeCecilified, actualAssemblyPath);

            VerifyAssembly(actualAssemblyPath, null, options);

            var actualIL = GetILFrom(actualAssemblyPath, methodSignature);
            Assert.That(actualIL, Is.EqualTo(expectedIL), $"Actual IL differs from expected.\nActual Assembly Path = {actualAssemblyPath}\nExpected IL:\n{expectedIL}\nActual IL:{actualIL}");
        }

        private void VerifyAssembly(string actualAssemblyPath, string expectedAssemblyPath, ResourceTestOptions options)
        {
            var dotnetRootPath = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrEmpty(dotnetRootPath))
            {
                Console.WriteLine($"Unable to resolve DOTNET_ROOT environment variable. Skping ilverify on {actualAssemblyPath}");
                return;
            }

            var ignoreErrorsArg = options.IgnoredILErrors != null ? $" -g {options.IgnoredILErrors}" : string.Empty;
            var ilVerifyStartInfo = new ProcessStartInfo
            {
                FileName = "ilverify",
                Arguments = $"""{actualAssemblyPath} -r "{dotnetRootPath}/packs/Microsoft.NETCore.App.Ref/{Environment.Version}/ref/net{Environment.Version.Major}.{Environment.Version.Minor}/*.dll"{ignoreErrorsArg}""",
                WindowStyle = ProcessWindowStyle.Normal,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var ilverifyProcess = new Process
            {
                StartInfo = ilVerifyStartInfo
            };

            var output = new List<string>();
            ilverifyProcess.OutputDataReceived += (_, arg) => output.Add(arg.Data);
            ilverifyProcess.ErrorDataReceived += (_, arg) => output.Add(arg.Data);

            ilverifyProcess.Start();
            ilverifyProcess.BeginOutputReadLine();
            ilverifyProcess.BeginErrorReadLine();

            if (!ilverifyProcess.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException($"ilverify ({ilverifyProcess.Id}) took more than 30 secs to process {actualAssemblyPath}");
            }

            if (ilverifyProcess.ExitCode != 0)
            {
                output.Add($"ilverify for {actualAssemblyPath} failed with exit code = {ilverifyProcess.ExitCode}.\n{(expectedAssemblyPath != null ? $"Expected path={expectedAssemblyPath}\n" : "")}");
                if (options.FailOnAssemblyVerificationErrors)
                {
                    throw new Exception(string.Join('\n', output));
                }

                TestContext.WriteLine(string.Join('\n', output));
            }
        }

        private void CecilifyAndExecute(Stream tbc, string outputAssemblyPath)
        {
            cecilifiedCode = Cecilfy(tbc);

            var references = Utils.GetTrustedAssembliesPath().Where(a => !a.Contains("mscorlib"));
            var refsToCopy = new List<string>
            {
                typeof(ILParser).Assembly.Location,
                typeof(TypeReference).Assembly.Location,
                typeof(TypeHelpers).Assembly.Location,
            };

            references = references.Concat(refsToCopy).ToList();

            var actualAssemblyGeneratorPath = Path.Combine(Path.GetTempPath(), $"CecilifierTests/{TestContext.CurrentContext.Test.MethodName}/{cecilifiedCode.GetHashCode()}/{TestContext.CurrentContext.Test.MethodName}");
            var cecilifierRunnerPath = CompilationServices.CompileExe(actualAssemblyGeneratorPath, cecilifiedCode, references.ToArray());

            Console.WriteLine("------- Cecilified Code -------");
            Console.WriteLine(cecilifiedCode);
            Console.WriteLine("^^^^^^^ Cecilified Code ^^^^^^^");

            Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath));
            CopyFilesNextToGeneratedExecutable(cecilifierRunnerPath, refsToCopy);
            Console.WriteLine("Cecil runner path: {0}", cecilifierRunnerPath);

            TestFramework.Execute("dotnet", cecilifierRunnerPath + " " + outputAssemblyPath);
        }

        private void CopyFilesNextToGeneratedExecutable(string cecilifierRunnerPath, List<string> refsToCopy)
        {
            var targetPath = Path.GetDirectoryName(cecilifierRunnerPath);
            foreach (var fileToCopy in refsToCopy)
            {
                File.Copy(fileToCopy, Path.Combine(targetPath, Path.GetFileName(fileToCopy)), true);
            }

            var sourceRuntimeConfigJson = Path.ChangeExtension(GetType().Assembly.Location, ".runtimeconfig.json");
            var targetRuntimeConfigJson = Path.ChangeExtension(cecilifierRunnerPath, ".runtimeconfig.json");

            File.Copy(sourceRuntimeConfigJson, targetRuntimeConfigJson, true);
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

        private void CompareAssemblies(string expectedAssemblyPath, string actualAssemblyPath, IAssemblyDiffVisitor visitor, Func<Instruction, Instruction, bool?> instructionComparer)
        {
            var comparer = new AssemblyComparer(expectedAssemblyPath, actualAssemblyPath);
            if (!comparer.Compare(visitor, instructionComparer))
            {
                Assert.Fail($"Expected and generated assemblies differs:\r\n\tExpected:  {comparer.First}\r\n\tGenerated: {comparer.Second}\r\n\r\n{visitor.Reason}\r\n\r\nCecilified Code:\r\n{cecilifiedCode}");
            }
        }

        protected Stream ReadResource(string resourceName, string type)
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
            return Cecilifier.Process(stream, new CecilifierOptions { References = Utils.GetTrustedAssembliesPath(), Naming = new DefaultNameStrategy() }).GeneratedCode.ReadToEnd();
        }
    }
}
