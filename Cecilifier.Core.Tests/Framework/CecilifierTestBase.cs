using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Cecilifier.Core.Tests.Framework.ILVerification;
using Cecilifier.Runtime;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using NUnit.Framework;
using ILVerify;

namespace Cecilifier.Core.Tests.Framework;

public record struct CecilifyResult(string CecilifiedCode, string CecilifiedAssemblyFilePath, string CecilifiedOutputAssemblyFilePath);

public class CecilifierTestBase
{
    private protected string cecilifiedCode;

    //TODO: Only OutputBasedTests tests anything other than Mono.Cecil.
    //      Do we need to run other tests (Integration for instance) for System.Reflection.Metadata also? Is there an easy way to do that?
    protected IILGeneratorApiDriver ApiDriver { get; set; } = new MonoCecilGeneratorDriver();

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

    class AssemblyResolver : IResolver
    {
        private readonly Dictionary<string, PEReader> assemblyCache = new();
        public PEReader ResolveAssembly(AssemblyNameInfo assemblyName)
        {
            if (assemblyCache.TryGetValue(assemblyName.Name, out var assembly))
                return assembly;
            
            var assemblyPath = referenceAssemblies.SingleOrDefault(assemblyPath => Path.GetFileName(Path.GetFileNameWithoutExtension(assemblyPath)) == assemblyName.Name);
            if (assemblyPath != null)
            {
                var resolvedAssembly = new PEReader(File.OpenRead(assemblyPath));
                assemblyCache[assemblyName.Name] = resolvedAssembly;
                return resolvedAssembly;
            }

            return null;
        }

        public PEReader ResolveModule(AssemblyNameInfo referencingAssembly, string fileName)
        {
            return null;
        }

        public AssemblyResolver(string dotnetRoot)
        {
            referenceAssemblies = Directory.GetFiles($"{dotnetRoot}/packs/Microsoft.NETCore.App.Ref/{Environment.Version}/ref/net{Environment.Version.Major}.{Environment.Version.Minor}", "*.dll");
        }

        private string[] referenceAssemblies;
    }
    
    private protected void VerifyAssembly(string actualAssemblyPath, string expectedAssemblyPath, CecilifyTestOptions options)
    {
        var dotnetRootPath = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrEmpty(dotnetRootPath))
        {
            Console.WriteLine($"Unable to resolve DOTNET_ROOT environment variable. Skping ilverify on {actualAssemblyPath}");
            return;
        }

        var v = new Verifier(new AssemblyResolver(dotnetRootPath), new VerifierOptions() { IncludeMetadataTokensInErrorMessages = true});
        v.SetSystemModuleName(new AssemblyNameInfo("mscorlib"));
        var assemblyReaderToVerify = new PEReader(new FileStream(actualAssemblyPath, FileMode.Open));
        var verifierResults = v.Verify(assemblyReaderToVerify).ToArray();
        
        if (verifierResults.Length > 0)
        {
            var ignoredErrorsSpan = options.IgnoredILErrors.AsSpan();
            Span<Range> splitPositions = stackalloc Range[ignoredErrorsSpan.Count('|') + 1];
            var ignoredErrorsCount = ignoredErrorsSpan.Split(splitPositions, '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            HashSet<VerifierError> ignoredErrors = new();
            foreach (var ignoredErrorNamePosition in splitPositions.Slice(0, ignoredErrorsCount))
            {
                ignoredErrors.Add(Enum.Parse<VerifierError>(ignoredErrorsSpan[ignoredErrorNamePosition]));
            }
            
            List<string> output = new();
            output.AddRange(verifierResults.Where(error => !ignoredErrors.Contains(error.Code)).Select(error => ILVerifierResult.From(error, assemblyReaderToVerify.GetMetadataReader()).GetErrorMessage()).ToArray());
            if (output.Count == 0)
                return;
            
            output.Add($"ilverify for {actualAssemblyPath} failed.\n{(expectedAssemblyPath != null ? $"Expected path={expectedAssemblyPath}\n" : "")}");

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
            // typeof(ILParser).Assembly.Location,
            // typeof(Mono.Cecil.TypeReference).Assembly.Location,
            typeof(TypeHelpers).Assembly.Location
        ];
        refsToCopy.AddRange(ApiDriver.AssemblyReferences);

        references = references.Concat(refsToCopy).ToList();

        var cecilifierRunnerPath = CompilationServices.InternalCompile(Path.Combine(testBasePath, "Runner"), cecilifiedCode, true, references.ToArray(), () =>
        {
            // ensures that the cecilified code will be compiled if any of the hashes changes:
            // 1. Snippet
            // 2. Cecilifier Runtime Assembly
            using var hasher = SHA1.Create();
            var cecilifiedBytes = Encoding.ASCII.GetBytes(cecilifiedCode);
            hasher.TransformBlock(cecilifiedBytes, 0, cecilifiedBytes.Length, cecilifiedBytes, 0);

            var cecilifierRuntimeAssemblyInfo = new FileInfo(typeof(TypeHelpers).Assembly.Location);
                
            var lastWriteBytes = BitConverter.GetBytes(cecilifierRuntimeAssemblyInfo.LastWriteTimeUtc.Ticks);
            hasher.TransformBlock(lastWriteBytes, 0, lastWriteBytes.Length, lastWriteBytes, 0);
                
            var cecilifierAssemblySize = BitConverter.GetBytes(cecilifierRuntimeAssemblyInfo.Length);
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
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(actualAssemblyPath);
        var method = assembly.MainModule.Types.SelectMany(t => t.Methods).SingleOrDefault(m => m.FullName == methodSignature);
        if (method == null)
        {
            Assert.Fail($"Method {methodSignature} could not be found in {actualAssemblyPath}");
        }

        return Formatter.FormatMethodBody(method).Replace("\t", "");
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
        return Cecilifier.Process(
            stream, 
            new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath(), Naming = new DefaultNameStrategy(), GeneratorApiDriver = ApiDriver }
            ).GeneratedCode.ReadToEnd();
    }

    protected void WithApiDriver(IILGeneratorApiDriver driver, Action action)
    {
        var previousDriver = ApiDriver;
        try
        {
            ApiDriver = driver;
            action();
        }
        finally
        {
            ApiDriver = previousDriver;
        }
    }

    public static IEnumerable AllILGenerators()
    {
        yield return new MonoCecilGeneratorDriver();
        yield return new SystemReflectionMetadataGeneratorDriver();
    }
}
