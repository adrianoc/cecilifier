using System.IO;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class StrictFieldDiffVisitor : IFieldDiffVisitor
    {
        private readonly TextWriter output;

        public StrictFieldDiffVisitor(TextWriter output)
        {
            this.output = output;
        }

        public bool VisitMissing(IMemberDefinition member, TypeDefinition target)
        {
            output.WriteLine("[{0}] Field {1} not found.", target.FullName, member.FullName);
            return false;
        }

        public bool VisitName(IMemberDefinition source, IMemberDefinition target)
        {
            output.WriteLine("Field simple name ('{0}') matches, but not FQN. Expected {1} got {2}.", source.Name, source.FullName, target.FullName);
            return false;
        }

        public bool VisitDeclaringType(IMemberDefinition source, IMemberDefinition target)
        {
            output.WriteLine("Declaring type differs. Expected '{0}' got '{1}'.", source.FullName, target.FullName);
            return false;
        }

        public bool VisitFieldType(FieldDefinition source, FieldDefinition target)
        {
            output.WriteLine("[{0}] Field type differs. Expected '{1}' got '{2}'.", target.FullName, source.FieldType.FullName, target.FieldType.FullName);
            return false;
        }

        public bool VisitAttributes(FieldDefinition source, FieldDefinition target)
        {
            output.WriteLine("[{0}] Field attributes differs. Expected '{1}' got '{2}'.", target.FullName, source.Attributes, target.Attributes);
            return false;
        }

        public bool VisitConstant(FieldDefinition source, FieldDefinition target)
        {
            output.WriteLine("[{0}] Field constant values differs. Expected '{1}' got '{2}'.", target.FullName, source.Constant, target.Constant);
            return false;
        }
    }
}
