using System;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Misc
{
	public class TypeInfo
	{
		public readonly string LocalVariable;
		public Action<string, BaseTypeDeclarationSyntax> CtorInjector;

		public TypeInfo(string localVariable, Action<string, BaseTypeDeclarationSyntax> ctorInjector = null)
		{
			LocalVariable = localVariable;
			CtorInjector = ctorInjector ?? delegate { };
		}

		public override string ToString()
		{
			return LocalVariable;
		}
	}
}