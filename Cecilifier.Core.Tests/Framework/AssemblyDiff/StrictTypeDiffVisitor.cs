using System.IO;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class StrictTypeDiffVisitor : BaseStrictDiffVisitor, ITypeDiffVisitor
    {
        public StrictTypeDiffVisitor(TextWriter output) : base(output)
        {
        }

        public bool VisitAttributes(TypeDefinition source, TypeDefinition target)
        {
            output.WriteLine("[{0}] Attributes differs from {1}. Expected '{2}' but got '{3}'.", target, source, source.Attributes, target.Attributes);
            return false;
        }

        public bool VisitMissing(TypeDefinition source, ModuleDefinition target)
        {
            if (Utils.compilerEmmitedAttributesToIgnore.Contains(source.FullName))
                return true;
            
            output.WriteLine("[{0}] Type {1} could not be found.", target.FileName, source.FullName);
            return false;
        }

        public bool VisitBaseType(TypeDefinition baseType, TypeDefinition target)
        {
            output.WriteLine("[{0}] Base types differs. Expected {1} but got {2}", target.FullName, baseType.FullName, target.BaseType != null ? target.BaseType.FullName : "null");
            return false;
        }

        public bool VisitCustomAttributes(TypeDefinition source, TypeDefinition target)
        {
            return Utils.CheckCustomAttributes(output, source, target);
        }

        public bool VisitGenerics(TypeDefinition source, TypeDefinition target)
        {
            return ValidateGenericParameters(source, target, source.GenericParameters, target.GenericParameters, source.Module.FileName, target.Module.FileName);
        }

        public IFieldDiffVisitor VisitMember(FieldDefinition field)
        {
            return new StrictFieldDiffVisitor(output);
        }

        public IMethodDiffVisitor VisitMember(MethodDefinition method)
        {
            return new StrictMethodDiffVisitor(output);
        }

        public IEventDiffVisitor VisitMember(EventDefinition @event)
        {
            return new StrictEventDiffVisitor(output);
        }

        public IPropertyDiffVisitor VisitMember(PropertyDefinition property)
        {
            return new StrictPropertyDiffVisitor(output);
        }

        public void VisitEnd(TypeDefinition type)
        {
        }
    }
}
