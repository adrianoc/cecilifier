using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
	interface IAssemblyDiffVisitor
	{
		bool VisitModules(AssemblyDefinition source, AssemblyDefinition target);
		ITypeDiffVisitor VisitType(TypeDefinition sourceType);
	}
}
