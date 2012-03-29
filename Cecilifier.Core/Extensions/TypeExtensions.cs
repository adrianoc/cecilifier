using System.Collections.Generic;
using Mono.Cecil.Cil;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Extensions
{
	public static class TypeExtensions
	{
		public static string FullyQualifiedName(this TypeSymbol type)
		{
			var format = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
			return type.ToDisplayString(format);
		}
		
		public static string FrameworkSimpleName(this TypeSymbol type)
		{
			return type.ToDisplayString(new SymbolDisplayFormat());
		}

		public static IEqualityComparer<VariableDefinition> Comparer
		{
			get { return new VariableDefinitionComparer(); }
		}
	}

	public class VariableDefinitionComparer: IEqualityComparer<VariableDefinition>
	{
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
	}
}
