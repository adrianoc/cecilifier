using System;
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
        public static string CompileDLL(string targetPath, string source, params string[] references)
        {
            return InternalCompile(targetPath, source, false, references);
        }

        public static string CompileExe(string targetPath, string source, params string[] references)
        {
            return InternalCompile(targetPath, source, true, references);
        }

        private static string InternalCompile(string targetPath, string source, bool exe, string[] references)
        {
            var targetFolder = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            var hash = BitConverter.ToString(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(source))).Replace("-", "");
            var outputFilePath = $"{targetPath}-{hash}.{(exe ? "exe" : "dll")}";
            if (File.Exists(outputFilePath))
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
            if (File.Exists(outputFilePath))
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
    }
}
