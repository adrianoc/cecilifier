using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Extensions
{
	static class TypeExtensions
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
	}
}
