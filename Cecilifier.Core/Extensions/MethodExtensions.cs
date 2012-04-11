using System;
using System.Linq;
using System.Reflection;
using Cecilifier.Core.AST;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Extensions
{
    static class MethodExtensions
    {
        public static string MangleName(this BaseMethodDeclarationSyntax method, SemanticModel sm)
        {
        	var methodSymbol = sm.GetDeclaredSymbol(method) as MethodSymbol;
			if (methodSymbol == null)
			{
				throw new Exception("Failled to retrieve method symbol for " + method);
			}

        	return methodSymbol.MangleName();
        }

    	public static string MangleName(this MethodSymbol method)
        {
    		return method.Parameters.Aggregate("", (acc, curr) => acc + curr.Type.Name.ToLower());
        }
    	
		public static int ParameterIndex(this MethodSymbol method, ParameterSymbol param)
		{
			return param.Ordinal + (method.IsStatic ? 0 : 1);
		}

		public static string Modifiers(this MethodSymbol method)
		{
			var bindingFlags = method.IsStatic ? BindingFlags.Static : BindingFlags.Instance;
			bindingFlags |= method.DeclaredAccessibility == Accessibility.Public ? BindingFlags.Public : BindingFlags.NonPublic;


			string res = "";
			var enumType = typeof (BindingFlags);
			foreach (BindingFlags flag in Enum.GetValues(enumType))
			{
				if (bindingFlags.HasFlag(flag))
				{
					res = res + "|" + enumType.FullName + "." + flag;
				}
			}
			
			return res.Length > 0 ? res.Substring(1) : String.Empty;
		}

    	public static string MethodResolverExpression(this MethodSymbol method, IVisitorContext ctx)
		{
			if (method.IsDefinedInCurrentType(ctx))
			{
				//FIXME: Keep the name of the variables used to construct types/members in a map
				return LocalVariableName(method);
			}

			var declaringTypeName = method.ContainingType.FullyQualifiedName();

			return String.Format("assembly.MainModule.Import(TypeHelpers.ResolveMethod(\"{0}\", \"{1}\", \"{2}\",{3}{4}))",
								 method.ContainingAssembly.AssemblyName.FullName,
								 declaringTypeName,
								 method.Name,
								 method.Modifiers(),
								 method.Parameters.Aggregate("", (acc, curr) => ", \"" + curr.Type.FullyQualifiedName() + "\""));
		}

    	public static string LocalVariableName(this MethodSymbol method)
    	{
    		return LocalVariableNameFor(method.ContainingType.Name, method.Name.Replace(".", ""), method.MangleName());
    	}

    	public static bool IsDefinedInCurrentType(this MethodSymbol method, IVisitorContext ctx)
		{
			return method.ContainingAssembly == ctx.SemanticModel.Compilation.Assembly;
		}

		private static string LocalVariableNameFor(string prefix, params string[] parts)
		{
			return parts.Aggregate(prefix, (acc, curr) => acc + "_" + curr);
		}
    }
}
