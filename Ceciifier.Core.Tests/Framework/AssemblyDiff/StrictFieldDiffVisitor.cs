using System.IO;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
	class StrictFieldDiffVisitor : IFieldDiffVisitor
	{
		private readonly TextWriter output;

		public StrictFieldDiffVisitor(TextWriter output)
		{
			this.output = output;
		}

		public bool VisitMissing(IMemberDefinition member, TypeDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Field {1} not found.", target.FullName, member.FullName));
			return false;
		}

		public bool VisitName(IMemberDefinition source, IMemberDefinition target)
		{
			output.WriteLine(string.Format("Field simple name ('{0}') matches, but not FQN. Expected {1} got {2}.", source.Name, source.FullName, target.FullName));
			return true;
		}

		public bool VisitDeclaringType(IMemberDefinition source, IMemberDefinition target)
		{
			output.WriteLine(string.Format("Declaring type differs. Expected '{0}' got '{1}'.", source.FullName, target.FullName));
			return true;
		}

		public bool VisitFieldType(FieldDefinition source, FieldDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Field type differs. Expected '{1}' got '{2}'.", target.FullName, source.FieldType.FullName, target.FieldType.FullName));
			return true;
		}

		public bool VisitAttributes(FieldDefinition source, FieldDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Type attributes differs. Expected '{1}' got '{2}'.", target.FullName, source.Attributes, target.Attributes));
			return true;
		}

		public bool VisitConstant(FieldDefinition source, FieldDefinition target)
		{
			output.WriteLine("[{0}] Field constant values differs. Expected '{1}' got '{2}'.", target.FullName , source.Constant, target.Constant);
			return true;
		}
	}
}
