using System;
using System.Collections.Generic;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
	static class TypeExtensions
	{
		public static string FullyQualifiedName(this ITypeSymbol type)
		{
			var format = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
			return type.ToDisplayString(format);
		}
		
		public static string FrameworkSimpleName(this ITypeSymbol type)
		{
			return type.ToDisplayString(new SymbolDisplayFormat());
		}

		public static string ResolverExpression(this ITypeSymbol type, IVisitorContext ctx)
		{
			if (type.IsDefinedInCurrentType(ctx))
			{
				//TODO: This assumes the type in question as already been visited.
				//		see: Types\ForwardTypeReference
				return ctx.ResolveTypeLocalVariable(type.Name);
			}

			return String.Format("assembly.MainModule.Import(TypeHelpers.ResolveType(\"{0}\", \"{1}\"))",
								 type.ContainingAssembly.Name,
								 type.FullyQualifiedName());
		}
	}

	public sealed class VariableDefinitionComparer: IEqualityComparer<VariableDefinition>
	{
		public static IEqualityComparer<VariableDefinition> Instance
		{
			get { return instance.Value; }
		}

		public bool Equals(VariableDefinition x, VariableDefinition y)
		{
			if (x == null && y == null) return true;
			if (x == null || y == null) return false;

			return x.Name == y.Name && x.VariableType.FullName == y.VariableType.FullName;
		}

		public int GetHashCode(VariableDefinition obj)
		{
			return obj.Name.GetHashCode() + 37*obj.VariableType.FullName.GetHashCode();
		}

		private static Lazy<IEqualityComparer<VariableDefinition>> instance = new Lazy<IEqualityComparer<VariableDefinition>>(delegate { return new VariableDefinitionComparer() ; });
	}
}
