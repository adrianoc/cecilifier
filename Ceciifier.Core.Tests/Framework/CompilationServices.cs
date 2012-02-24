using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;

namespace Ceciifier.Core.Tests.Framework
{
	class CompilationServices
	{
		public static string CompileDLL(string source, params Assembly[] references)
		{
			return InternalCompile(source, false, references);
		}
		
		public static string CompileExe(string source, params Assembly[] references)
		{
			return InternalCompile(source, true, references);
		}

		private static string InternalCompile(string source, bool exe, params Assembly[] references)
		{
			var provider = new CSharpCodeProvider();
			var parameters = new CompilerParameters();

			var tempFolder = Path.Combine(Path.GetTempPath(), "CecilifierTests_" + source.GetHashCode());
			if (!Directory.Exists(tempFolder))
			{
				Directory.CreateDirectory(tempFolder);
			}

			parameters.ReferencedAssemblies.AddRange(CopyReferencedAssembliesTo(tempFolder, references));

			parameters.OutputAssembly = Path.Combine(tempFolder, Path.GetRandomFileName() + (exe ? ".exe" : ".dll"));
			parameters.GenerateExecutable = exe;
			parameters.IncludeDebugInformation = true;

			var results = provider.CompileAssemblyFromSource(parameters, source);

			if (results.Errors.Count > 0)
			{
				throw new Exception(results.Errors.OfType<CompilerError>().Aggregate("", (acc, curr) => acc + "\r\n" + curr.ToString()) + "\r\n\r\n" + source);
			}

			return results.PathToAssembly;
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
