using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Cecilifier.Core.Tests.Framework
{
    internal class CompilationServices
    {
        public static string CompileDLL(string targetPath, string source, string[] preprocessorSymbols, params string[] references)
        {
            return InternalCompile(targetPath, source, false, preprocessorSymbols, references);
        }

        public static string CompileExe(string targetPath, string source, string[] preprocessorSymbols, params string[] references)
        {
            return InternalCompile(targetPath, source, true, preprocessorSymbols, references);
        }

        private static string InternalCompile(string targetPath, string source, bool exe, string[] preprocessorSymbols, string[] references)
        {
            var targetFolder = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }
            
            var hash = HashFor(source, preprocessorSymbols);

            var outputFilePath = $"{targetPath}-{hash}.{(exe ? "exe" : "dll")}";
            if (File.Exists(outputFilePath))
            {
                return outputFilePath;
            }

            var syntaxTree = SyntaxFactory.ParseSyntaxTree(SourceText.From(source), new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: preprocessorSymbols));

            var compilationOptions = new CSharpCompilationOptions(
                exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true);

            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(outputFilePath),
                new[] { syntaxTree },
                references.Select(r => MetadataReference.CreateFromFile(r)).ToArray(),
                compilationOptions);

            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new Exception(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Aggregate("", (acc, curr) => acc + "\r\n" + curr.ToString()) + "\r\n\r\n" + source);
            }

            using (var outputAssembly = File.Create(outputFilePath))
            {
                compilation.Emit(outputAssembly);
            }

            return outputFilePath;
        }

        public static string InternalCompile(string targetPath, string source, bool exe, string[] references, Func<string> computeCacheKey)
        {
            var targetFolder = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            var hash = computeCacheKey();
            var outputFilePath = $"{targetPath}-{hash}.{(exe ? "exe" : "dll")}";
            if (File.Exists(outputFilePath) && new FileInfo(outputFilePath).Length > 0)
            {
                return outputFilePath;
            }

            var syntaxTree = SyntaxFactory.ParseSyntaxTree(SourceText.From(source), new CSharpParseOptions(LanguageVersion.Preview));

            var compilationOptions = new CSharpCompilationOptions(
                exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true);
            
            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(outputFilePath),
                new[] { syntaxTree },
                references.Select(r => MetadataReference.CreateFromFile(r)).ToArray(),
                compilationOptions);

            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new Exception(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Aggregate("", (acc, curr) => acc + "\r\n" + curr.ToString()) + "\r\n\r\n" + source);
            }

            using var outputAssembly = File.Create(outputFilePath);
            using var outputPdb = File.Create(Path.ChangeExtension(outputFilePath, ".pdb"));
            compilation.Emit(outputAssembly, outputPdb);

            return outputFilePath;
        }
        
        private static string HashFor(string source, string[] preprocessorSymbols)
        {
            using var hasher = SHA1.Create();
            
            hasher.Initialize();
            var sourceBytes = Encoding.ASCII.GetBytes(source);
            hasher.TransformBlock(Encoding.ASCII.GetBytes(source), 0, sourceBytes.Length, null, 0);
            foreach (var preprocessorSymbol in preprocessorSymbols)
                hasher.TransformBlock(Encoding.ASCII.GetBytes(preprocessorSymbol), 0, preprocessorSymbol.Length, null, 0);
            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            return BitConverter.ToString(hasher.Hash).Replace("-", "");
        }
    }
}
