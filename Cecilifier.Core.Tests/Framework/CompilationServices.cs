using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Cecilifier.Core.Tests.Framework
{
	class CompilationServices
	{
		public static string CompileDLL(string targetPath, string source, params string[] references)
		{
			return InternalCompile(targetPath, source, false, references);
		}
		
		public static string CompileExe(string source, params string[] references)
		{
			return InternalCompile(source, true, references);
		}

		private static string InternalCompile(string targetPath, string source, bool exe, params string[] references)
		{
			var targetFolder = Path.GetDirectoryName(targetPath);
			if (!Directory.Exists(targetFolder))
			{
				Directory.CreateDirectory(targetFolder);
			}

		    var hash = BitConverter.ToString(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(source))).Replace("-", "");
		    var outputFilePath = $"{targetPath}-{hash}.{(exe ? "exe" : "dll")}";

		    if (File.Exists(outputFilePath))
		        return outputFilePath;

			var syntaxTree = SyntaxFactory.ParseSyntaxTree(SourceText.From(source), new CSharpParseOptions());
			
			var compilationOptions = new CSharpCompilationOptions(
				exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: OptimizationLevel.Release);
			
			var compilation=  CSharpCompilation.Create(
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

		private static string InternalCompile(string source, bool exe, params string[] references)
		{
			var tempFolder = Path.Combine(Path.GetTempPath(), "CecilifierTests_" + source.GetHashCode());
			if (!Directory.Exists(tempFolder))
			{
				Directory.CreateDirectory(tempFolder);
			}

			return InternalCompile(Path.Combine(tempFolder, Path.GetRandomFileName()), source, exe, references);
		}

		private static string[] CopyReferencedAssembliesTo(string targetFolder, Assembly[] references)
		{
			var referencedAssemblies = new string[references.Length];

			int curr = 0;
			Array.ForEach(references, @ref =>
			{
				var assemblyPath = Path.Combine(targetFolder, Path.GetFileName(@ref.Location));
				File.Copy(@ref.Location, assemblyPath, true);
				referencedAssemblies[curr++] = assemblyPath;
			});

			return referencedAssemblies;
		}
	}
}
