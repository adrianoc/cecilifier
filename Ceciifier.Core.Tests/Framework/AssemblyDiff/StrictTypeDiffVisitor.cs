using System.IO;
using Mono.Cecil;

namespace Ceciifier.Core.Tests.Framework.AssemblyDiff
{
	class StrictTypeDiffVisitor : ITypeDiffVisitor
	{
		private readonly TextWriter output;

		public StrictTypeDiffVisitor(TextWriter output)
		{
			this.output = output;
		}

		public bool VisitAttributes(TypeDefinition source, TypeDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Attributes differs from {1}. Expected '{2}' but got '{3}'.", target, source, source.Attributes, target.Attributes));
			return false;
		}

		public bool VisitMissing(TypeDefinition source, ModuleDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Type {1} could not be found.", target.FullyQualifiedName, source.FullName));
			return false;
		}

		public bool VisitBaseType(TypeDefinition baseType, TypeDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Base types differs. Expected {1} but got {2}", target.FullName, baseType.FullName, target.BaseType != null ? target.BaseType.FullName : "null" ));
			return false;
		}

		public IFieldDiffVisitor VisitMember(FieldDefinition field)
		{
			return new StrictFieldDiffVisitor(output);
		}

		public IMethodDiffVisitor VisitMember(MethodDefinition method)
		{
			return new StrictMethodDiffVisitor(output);
		}

		public void VisitEnd(TypeDefinition type)
		{
		}
	}
}
