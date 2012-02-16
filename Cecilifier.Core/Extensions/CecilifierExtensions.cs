using System;

namespace Cecilifier.Core.Extensions
{
	public static class CecilifierExtensions
	{
		public static string CamelCase(this string str)
		{
			return str.Length > 1
							? (Char.ToUpper(str[0]) + str.Substring(1)) 
							: str;
		}

		public static string AppendModifier(this string to, string modifier)
		{
			if (string.IsNullOrWhiteSpace(modifier)) return to;

			return to + " | " + modifier;
		}

		public static string AsCecilApplication(this string cecilSnipet)
		{
			return string.Format(
					 @"using Mono.Cecil;
					   using Mono.Cecil.Cil;
                       using System; 
					   using System.Linq;
               
					   public class SnipetRunner
					   {{
						   public static void Main(string[] args)
						   {{
							   var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""name"", Version.Parse(""1.0.0.0"")), ""moduleName"", ModuleKind.Console);
							   {0}
							   assembly.Write(args[0]);                              
						   }}

						   private static MethodReference DefaultCtorFor(TypeDefinition type, AssemblyDefinition assembly)
						   {{
								return type.Methods.Where(m => m.IsConstructor && m.Parameters.Count == 0).Single();
  						   }}

                        }}
                      ", cecilSnipet);
		}
	}
}
