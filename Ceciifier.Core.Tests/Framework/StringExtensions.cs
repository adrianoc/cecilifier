namespace Ceciifier.Core.Tests.Framework
{
	public static class StringExtensions
	{
		public static string AsCecilApplication(this string cecilSnipet)
		{

			return string.Format(
					 @"using Mono.Cecil;
                       using System; 
               
					   public class SnipetRunner
					   {{
						   public static void Main(string[] args)
						   {{
							   var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""name"", Version.Parse(""1.0.0.0"")), ""moduleName"", ModuleKind.Console);
							   {0}
							   assembly.Write(args[0]);                              
						   }}
                        }}
                      ", cecilSnipet);
		}
	}
}
