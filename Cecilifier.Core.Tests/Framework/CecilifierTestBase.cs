using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Runtime;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Framework;

public record struct CecilifyResult(string CecilifiedCode, string CecilifiedAssemblyFilePath, string CecilifiedOutputAssemblyFilePath);

public class CecilifierTestBase
{
    private protected string cecilifiedCode;

    [SetUp]
    public void Setup()
    {
        cecilifiedCode = string.Empty;
        Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
    }

    private protected static string CompileExpectedTestAssembly(string cecilifiedAssemblyPath, BuildType buildType, string tbc)
    {
        var targetPath = Path.Combine(Path.GetDirectoryName(cecilifiedAssemblyPath), Path.GetFileNameWithoutExtension(cecilifiedAssemblyPath) + "-Expected");

        return buildType == BuildType.Exe
            ? CompilationServices.CompileExe(targetPath, tbc, ReferencedAssemblies.GetTrustedAssembliesPath())
            : CompilationServices.CompileDLL(targetPath, tbc, ReferencedAssemblies.GetTrustedAssembliesPath());
    }

    private protected void VerifyAssembly(string actualAssemblyPath, string expectedAssemblyPath, CecilifyTestOptions options)
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

            if (options.CecilifiedCode != null)
            {
                output.Add($"\n---------------------\nCecilified code:\n\n{options.CecilifiedCode}");
            }
            
            if (options.FailOnAssemblyVerificationErrors)
            {
                throw new Exception(string.Join('\n', output));
            }

            TestContext.WriteLine($"ilverify failed but test was configured to ignore such failures:\n\t{string.Join('\n', output)}");
        }
    }

    protected CecilifyResult CecilifyAndExecute(Stream tbc, string testBasePath)
    {
        cecilifiedCode = Cecilfy(tbc);

        var references = ReferencedAssemblies.GetTrustedAssembliesPath().Where(a => !a.Contains("mscorlib"));
        List<string> refsToCopy = [
            typeof(ILParser).Assembly.Location,
            typeof(TypeReference).Assembly.Location,
            typeof(TypeHelpers).Assembly.Location
        ];

        references = references.Concat(refsToCopy).ToList();

        var cecilifierRunnerPath = CompilationServices.InternalCompile(Path.Combine(testBasePath, "Runner"), cecilifiedCode, true, references.ToArray(), () =>
        {
            // ensures that the cecilified code will be compiled if any of the hashes changes:
            // 1. Snippet
            // 2. Cecilifier Core Assembly
            using var hasher = SHA1.Create();
            var cecilifiedBytes = Encoding.ASCII.GetBytes(cecilifiedCode);
            hasher.TransformBlock(cecilifiedBytes, 0, cecilifiedBytes.Length, cecilifiedBytes, 0);

            var cecilifierCoreAssemblyInfo = new FileInfo(typeof(Cecilifier).Assembly.Location);
                
            var lastWriteBytes = BitConverter.GetBytes(cecilifierCoreAssemblyInfo.LastWriteTimeUtc.Ticks);
            hasher.TransformBlock(lastWriteBytes, 0, lastWriteBytes.Length, lastWriteBytes, 0);
                
            var cecilifierAssemblySize = BitConverter.GetBytes(cecilifierCoreAssemblyInfo.Length);
            hasher.TransformFinalBlock(cecilifierAssemblySize, 0, cecilifierAssemblySize.Length);

            return BitConverter.ToString(hasher.Hash!).Replace("-", "");
        });

        var outputAssemblyPath = OutputAssemblyPath(Path.GetFileNameWithoutExtension(testBasePath));
        var testCompilationResult = new CecilifyResult(cecilifiedCode, cecilifierRunnerPath, outputAssemblyPath);
        if (File.Exists(outputAssemblyPath))
            return testCompilationResult;
            
        CopyFilesNextToGeneratedExecutable(cecilifierRunnerPath, refsToCopy);

        try
        {
            TestFramework.Execute("dotnet", $"{cecilifierRunnerPath} {outputAssemblyPath}");
        }
        catch (Exception)
        {
            Console.WriteLine($"Cecilified Code:\n{cecilifiedCode}\nCecilified Assembly: {cecilifierRunnerPath}\nCecilified Output Assembly:{outputAssemblyPath}");
            throw;
        }

        return testCompilationResult;

        string OutputAssemblyPath(string assemblyFileName)
        {
            // adds the hash of the cecilified binary (compiled cecilified code) to the name of the assembly
            // to be generated by it. This is used to avoid running the cecilified binary if it had not changed.
            Span<byte> cecilifierRunnerSpan = stackalloc byte[256];
            if (!Encoding.ASCII.TryGetBytes(cecilifierRunnerPath, cecilifierRunnerSpan, out _))
                throw new Exception();

            Span<byte> cecilifierRunnerHash = stackalloc byte[32];
            if (!SHA1.TryHashData(cecilifierRunnerSpan, cecilifierRunnerHash, out var cecilifierRunnerHashSize))
                throw new Exception();

            return Path.Combine(testBasePath, $"{assemblyFileName}-Cecilified-{BitConverter.ToString(cecilifierRunnerHash.ToArray()).Replace("-", "")}.dll");
        }
    }

    protected void CopyFilesNextToGeneratedExecutable(string cecilifierRunnerPath, List<string> refsToCopy)
    {
        var targetPath = Path.GetDirectoryName(cecilifierRunnerPath);
        foreach (var fileToCopy in refsToCopy)
        {
            File.Copy(fileToCopy, Path.Combine(targetPath, Path.GetFileName(fileToCopy)), true);
        }

        var sourceRuntimeConfigJson = Path.ChangeExtension(GetType().Assembly.Location, Constants.Common.RuntimeConfigJsonExt);
        var targetRuntimeConfigJson = Path.ChangeExtension(cecilifierRunnerPath, Constants.Common.RuntimeConfigJsonExt);

        File.Copy(sourceRuntimeConfigJson, targetRuntimeConfigJson, true);
    }

    private protected string GetILFrom(string actualAssemblyPath, string methodSignature)
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

    private protected static string ReadToEnd(Stream tbc)
    {
        tbc.Seek(0, SeekOrigin.Begin);
        return new StreamReader(tbc).ReadToEnd();
    }
    
    protected string GetTestOutputBaseFolderFor(string testCategory) => Path.Combine(Path.GetTempPath(), "CecilifierTests", testCategory, TestContext.CurrentContext.Test.MethodName!);

    internal void CompareAssemblies(string expectedAssemblyPath, string actualAssemblyPath, IAssemblyDiffVisitor visitor, Func<Instruction, Instruction, bool?> instructionComparer)
    {
        try
        {
            var comparer = new AssemblyComparer(expectedAssemblyPath, actualAssemblyPath);
            if (!comparer.Compare(visitor, instructionComparer))
            {
                Assert.Fail($"Expected and generated assemblies differs:\r\n\tExpected:  {comparer.First}\r\n\tGenerated: {comparer.Second}\r\n\r\n{visitor.Reason}\r\n\r\nCecilified Code:\r\n{cecilifiedCode}");
            }
        }
        catch (Exception exception)
        {
            Assert.Fail($"Exception caught while comparing assemblies:\n\t{expectedAssemblyPath}\n\t{actualAssemblyPath}\n\n{exception}");
        }
    }

    private string Cecilfy(Stream stream)
    {
        stream.Position = 0;
        return Cecilifier.Process(stream, new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath(), Naming = new DefaultNameStrategy() }).GeneratedCode.ReadToEnd();
    }
}
