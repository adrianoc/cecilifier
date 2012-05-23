using Cecilifier.Core.AST;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Extensions
{
	static class FieldExtensions
	{
		public static string FieldResolverExpression(this FieldSymbol field, IVisitorContext context)
		{
			if (field.IsDefinedInCurrentType(context))
			{
				return "fld_" + field.ContainingType.Name.CamelCase() + "_" + field.Name.CamelCase();
			}

			var declaringTypeName = field.ContainingType.FullyQualifiedName();

			return string.Format("assembly.MainModule.Import(TypeHelpers.ResolveField(\"{0}\",\"{1}\"))", declaringTypeName, field.Name);
		}
	}
}
