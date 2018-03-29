using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CSharp;

namespace Cecilifier.Core.Tests.Framework
{
	class CompilationServices
	{
		public static string CompileDLL(string source, params Assembly[] references)
		{
			return InternalCompile(source, false, references);
		}

		public static string CompileDLL(string targetPath, string source, params Assembly[] references)
		{
			return InternalCompile(targetPath, source, false, references);
		}
		
		public static string CompileExe(string source, params Assembly[] references)
		{
			return InternalCompile(source, true, references);
		}

		private static string InternalCompile(string targetPath, string source, bool exe, params Assembly[] references)
		{
			var provider = new CSharpCodeProvider();
			var parameters = new CompilerParameters();

			var targetFolder = Path.GetDirectoryName(targetPath);
			if (!Directory.Exists(targetFolder))
			{
				Directory.CreateDirectory(targetFolder);
			}

		    var hash = BitConverter.ToString(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(source))).Replace("-", "");
		    var outputFilePath = $"{targetPath}-{hash}.{(exe ? "exe" : "dll")}";

		    if (File.Exists(outputFilePath))
		        return outputFilePath;

            parameters.ReferencedAssemblies.AddRange(CopyReferencedAssembliesTo(targetFolder, references));
            parameters.OutputAssembly = outputFilePath;
			parameters.GenerateExecutable = exe;
			parameters.IncludeDebugInformation = true;
			parameters.CompilerOptions = "/o+ /unsafe";

			var results = provider.CompileAssemblyFromSource(parameters, source);

			if (results.Errors.Count > 0)
			{
				throw new Exception(results.Errors.OfType<CompilerError>().Aggregate("", (acc, curr) => acc + "\r\n" + curr.ToString()) + "\r\n\r\n" + source);
			}

			return results.PathToAssembly;
		}

		private static string InternalCompile(string source, bool exe, params Assembly[] references)
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
