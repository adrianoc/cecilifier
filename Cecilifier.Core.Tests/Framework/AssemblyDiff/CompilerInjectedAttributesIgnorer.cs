using System.Collections.Generic;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public class CompilerInjectedAttributesIgnorer : IAssemblyDiffVisitor, ITypeDiffVisitor
    {
        public bool VisitModules(AssemblyDefinition source, AssemblyDefinition target)
        {
            return other.VisitModules(source, target);
        }

        public ITypeDiffVisitor VisitType(TypeDefinition sourceType)
        {
            typeVisitor = other.VisitType(sourceType);
            return this;
        }

        public bool VisitAttributes(TypeDefinition source, TypeDefinition target)
        {
            return typeVisitor.VisitAttributes(source, target);
        }

        public bool VisitMissing(TypeDefinition source, ModuleDefinition target)
        {
            return toBeIgnored.Contains(source.FullName) || typeVisitor.VisitMissing(source, target);
        }

        public bool VisitBaseType(TypeDefinition baseType, TypeDefinition target)
        {
            return VisitBaseType(baseType, target);
        }

        public bool VisitCustomAttributes(TypeDefinition source, TypeDefinition target)
        {
            return typeVisitor.VisitCustomAttributes(source, target);
        }

        public bool VisitGenerics(TypeDefinition source, TypeDefinition target)
        {
            return typeVisitor.VisitGenerics(source, target);
        }

        public IFieldDiffVisitor VisitMember(FieldDefinition field)
        {
            return typeVisitor.VisitMember(field);
        }

        public IMethodDiffVisitor VisitMember(MethodDefinition method)
        {
            return typeVisitor.VisitMember(method);
        }

        public IEventDiffVisitor VisitMember(EventDefinition @event)
        {
            return typeVisitor.VisitMember(@event);
        }

        public string Reason => other.Reason;

        internal CompilerInjectedAttributesIgnorer(string[] toBeIgnored)
        {
            other = new StrictAssemblyDiffVisitor();
            this.toBeIgnored = new HashSet<string>(toBeIgnored);
        }

        private IAssemblyDiffVisitor other;
        private ITypeDiffVisitor typeVisitor;
        private ISet<string> toBeIgnored;
    }
}
