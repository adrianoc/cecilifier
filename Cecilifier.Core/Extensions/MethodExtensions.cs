using System;
using System.Linq;
using System.Reflection;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions
{
    static class MethodExtensions
    {
        public static string MangleName(this BaseMethodDeclarationSyntax method, SemanticModel sm)
        {
        	var methodSymbol = (IMethodSymbol) sm.GetDeclaredSymbol(method);
			if (methodSymbol == null)
			{
				throw new Exception("Failled to retrieve method symbol for " + method);
			}

        	return methodSymbol.MangleName();
        }

    	public static string MangleName(this IMethodSymbol method)
        {
    		return method.Parameters.Aggregate("", (acc, curr) => acc + curr.Type.Name.ToLower());
        }
    	
		public static int ParameterIndex(this IMethodSymbol method, IParameterSymbol param)
		{
			return param.Ordinal + (method.IsStatic ? 0 : 1);
		}

		public static string Modifiers(this IMethodSymbol method)
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

    	public static string MethodResolverExpression(this IMethodSymbol method, IVisitorContext ctx)
		{
			if (method.IsDefinedInCurrentType(ctx))
			{
				//FIXME: Keep the name of the variables used to construct types/members in a map
				return LocalVariableName(method);
			}

			var declaringTypeName = method.ContainingType.FullyQualifiedName();

			return String.Format("assembly.MainModule.Import(TypeHelpers.ResolveMethod(\"{0}\", \"{1}\", \"{2}\",{3}{4}))",
								 method.ContainingAssembly.Name,
								 declaringTypeName,
								 method.Name,
								 method.Modifiers(),
								 method.Parameters.Aggregate("", (acc, curr) => ", \"" + curr.Type.FullyQualifiedName() + "\""));
		}

    	public static string LocalVariableName(this IMethodSymbol method)
    	{
    		return LocalVariableNameFor(method.ContainingType.Name, method.Name.Replace(".", ""), method.MangleName());
    	}

		public static string LocalVariableNameFor(string prefix, params string[] parts)
		{
			return parts.Aggregate(prefix, (acc, curr) => acc + "_" + curr);
		}
    }
}
