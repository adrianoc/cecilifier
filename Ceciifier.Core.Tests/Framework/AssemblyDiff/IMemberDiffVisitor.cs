using Mono.Cecil;

namespace Ceciifier.Core.Tests.Framework.AssemblyDiff
{
	interface IMemberDiffVisitor
	{
		bool VisitMissing(IMemberDefinition member, TypeDefinition target);
		bool VisitName(IMemberDefinition source, IMemberDefinition target);
		bool VisitDeclaringType(IMemberDefinition source, IMemberDefinition target);
	}
}
