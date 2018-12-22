using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions
{
    static class MethodExtensions
    {
        public static string MangleName(this BaseMethodDeclarationSyntax method, SemanticModel sm)
        {
        	var methodSymbol = (IMethodSymbol) ModelExtensions.GetDeclaredSymbol(sm, method);
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
			//if (method.IsDefinedInCurrentType(ctx) && method.MethodKind == MethodKind.Ordinary)
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
	    
		public static string MethodModifiersToCecil(this SyntaxTokenList modifiers, Func<string, IList<SyntaxToken>, string, string>  modifiersToCecil, string specificModifiers = null, IMethodSymbol methodSymbol = null)
		{
			var modifiersStr = MapExplicitModifiers(modifiers);

			var defaultAccessibility = "Private";
			if (modifiersStr == string.Empty && methodSymbol != null)
			{
				if (IsExplicitMethodImplementation(methodSymbol))
				{
					modifiersStr = "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final";
				}
				else
				{
					var lastDeclaredIn = methodSymbol.FindLastDefinition();
					if (lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface)
					{
						modifiersStr = "MethodAttributes.Virtual | MethodAttributes.NewSlot | " + (lastDeclaredIn.ContainingType == methodSymbol.ContainingType ? "MethodAttributes.Abstract" : "MethodAttributes.Final");
						defaultAccessibility = lastDeclaredIn.ContainingType == methodSymbol.ContainingType ? "Public" : "Private";
					}
				}
			}

			var validModifiers = RemoveSourceModifiersWithNoILEquivalent(modifiers);

			var cecilModifiersStr = modifiersToCecil("MethodAttributes", validModifiers.ToList(), defaultAccessibility);

			if (specificModifiers != null)
				cecilModifiersStr += $"| {specificModifiers}";
			
			return cecilModifiersStr + " | MethodAttributes.HideBySig".AppendModifier(modifiersStr);
		}

	    private static bool IsExplicitMethodImplementation(IMethodSymbol methodSymbol)
	    {
		    return methodSymbol.ExplicitInterfaceImplementations.Count() > 0;
	    }

		private static string MapExplicitModifiers(SyntaxTokenList modifiers)
		{
			foreach (var mod in modifiers)
			{
				switch (mod.Kind())
				{
					case SyntaxKind.VirtualKeyword:  return "MethodAttributes.Virtual | MethodAttributes.NewSlot";
					case SyntaxKind.OverrideKeyword: return "MethodAttributes.Virtual";
					case SyntaxKind.AbstractKeyword: return "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Abstract";
					case SyntaxKind.SealedKeyword:   return "MethodAttributes.Final";
					case SyntaxKind.NewKeyword:      return "??? new ??? dont know yet!";
				}
			}
			return string.Empty;
		}

	    private static IEnumerable<SyntaxToken> RemoveSourceModifiersWithNoILEquivalent(SyntaxTokenList modifiers)
	    {
		    return modifiers.Where(
			    mod => (mod.Kind() != SyntaxKind.OverrideKeyword 
			            && mod.Kind() != SyntaxKind.AbstractKeyword 
			            && mod.Kind() != SyntaxKind.VirtualKeyword 
			            && mod.Kind() != SyntaxKind.SealedKeyword));
	    }
    }
}
