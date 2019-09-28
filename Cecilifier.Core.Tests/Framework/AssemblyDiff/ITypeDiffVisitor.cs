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
    }
}
