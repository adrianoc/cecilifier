using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
	interface IFieldDiffVisitor : IMemberDiffVisitor
	{
		bool VisitFieldType(FieldDefinition source, FieldDefinition target);
		bool VisitAttributes(FieldDefinition source, FieldDefinition target);
		bool VisitConstant(FieldDefinition source, FieldDefinition target);
	}
}
