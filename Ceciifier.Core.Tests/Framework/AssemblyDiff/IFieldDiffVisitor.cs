using Mono.Cecil;

namespace Ceciifier.Core.Tests.Framework.AssemblyDiff
{
	interface IFieldDiffVisitor : IMemberDiffVisitor
	{
		bool VisitFieldType(FieldDefinition source, FieldDefinition target);
		bool VisitAttributes(FieldDefinition source, FieldDefinition target);
	}
}
