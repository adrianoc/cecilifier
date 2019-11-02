using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public interface ITypeDiffVisitor
    {
        bool VisitAttributes(TypeDefinition source, TypeDefinition target);
        bool VisitMissing(TypeDefinition source, ModuleDefinition target);
        bool VisitBaseType(TypeDefinition baseType, TypeDefinition target);
        bool VisitCustomAttributes(TypeDefinition source, TypeDefinition target);
        bool VisitGenerics(TypeDefinition source, TypeDefinition target);

        IFieldDiffVisitor VisitMember(FieldDefinition field);
        IMethodDiffVisitor VisitMember(MethodDefinition method);
        IEventDiffVisitor VisitMember(EventDefinition @event);
    }

    public interface IEventDiffVisitor
    {
        EventDefinition VisitEvent(EventDefinition sourceEvent, TypeDefinition target);
        
        bool VisitType(EventDefinition source, EventDefinition target);
        bool VisitAttributes(EventDefinition source, EventDefinition target);
        
        bool VisitAccessors(EventDefinition source, EventDefinition target);

        bool VisitCustomAttributes(EventDefinition source, EventDefinition target);
    }
}
