﻿using System;
using System.Linq;
using Cecilifier.Core.Misc;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

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


		public static string MapModifier(this SyntaxToken modifier, string targetEnum)
		{
            switch (modifier.Kind)
		    {
                case SyntaxKind.ProtectedKeyword: return targetEnum + ".Family";
                case SyntaxKind.InternalKeyword: return targetEnum + "." + (modifier.Parent.Kind == SyntaxKind.ClassDeclaration ? "NotPublic" : "Assembly");
		    }

            return targetEnum + "." + modifier.ValueText.CamelCase();
		}

		public static string AppendModifier(this string to, string modifier)
		{
            if (string.IsNullOrWhiteSpace(modifier)) return to;
            if (string.IsNullOrEmpty(to)) return modifier;

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

						   private static MethodReference DefaultCtorFor(TypeDefinition type)
						   {{
								var ctor = type.Methods.Where(m => m.IsConstructor && m.Parameters.Count == 0).SingleOrDefault();
                                return ctor ?? DefaultCtorFor(type.BaseType.Resolve()); 
  						   }}

                           private static System.Reflection.MethodInfo ResolveMethod(string assemblyName, string declaringTypeName, string methodName, params string[] paramTypes)
                           {{
                                var containingAssembly = System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(assemblyName));
                                var declaringType = containingAssembly.GetType(declaringTypeName);
                                return declaringType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, paramTypes.Select(typeName => Type.GetType(typeName)).ToArray(), null);
                           }}
                        }}
                      ", cecilSnipet);
		}

		public static MethodSymbol FindLastDefinition(this MethodSymbol self)
		{
            if (self == null) return null;
			return FindLastDefinition(self, self.ContainingType) ?? self;
		}

		public static BaseTypeDeclarationSyntax ResolveDeclaringType(this SyntaxNode node)
		{
			return new TypeDeclarationResolver().Resolve(node);
		}

		private static MethodSymbol FindLastDefinition(MethodSymbol method, NamedTypeSymbol toBeChecked)
		{
			if (toBeChecked == null) return null;

			var found = toBeChecked.GetMembers().OfType<MethodSymbol>().Where(candidate => CompareMethods(candidate, method)).SingleOrDefault();
			if (found == method || found == null)
			{
				found = FindLastDefinition(method, toBeChecked.Interfaces);
				found = found ?? FindLastDefinition(method, toBeChecked.BaseType);
			}

			return found;
		}

		private static MethodSymbol FindLastDefinition(MethodSymbol method, ReadOnlyArray<NamedTypeSymbol> implementedItfs)
		{
			foreach(var itf in implementedItfs)
			{
				var found = FindLastDefinition(method, itf);
				if (found != null) return found;
			}

			return null;
		}

		private static bool CompareMethods(MethodSymbol lhs, MethodSymbol rhs)
		{
			if (lhs.Name != rhs.Name) return false;

			if (lhs.ReturnType != rhs.ReturnType) return false;
			
			if (lhs.Parameters.Count != rhs.Parameters.Count) return false;
			for(int i = 0; i < lhs.Parameters.Count; i++)
			{
				if (lhs.Parameters[i].Type != rhs.Parameters[i].Type) return false;
			}

			return true;
		}
	}
}
