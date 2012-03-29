using System;
using System.Linq;
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

    	public static string MethodResolverExpression(this MethodSymbol method, IVisitorContext ctx)
		{
			if (method.IsDefinedInCurrentType(ctx))
			{
				//FIXME: Keep the name of the variables used to construct types/members in a map
				return LocalVariableName(method);
			}

			var declaringTypeName = method.ContainingType.FullyQualifiedName();

			return String.Format("assembly.MainModule.Import(ResolveMethod(\"{0}\", \"{1}\", \"{2}\"{3}))",
								 method.ContainingAssembly.AssemblyName.FullName,
								 declaringTypeName,
								 method.Name,
								 method.Parameters.Aggregate("", (acc, curr) => ", \"" + curr.Name + "\""));
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
