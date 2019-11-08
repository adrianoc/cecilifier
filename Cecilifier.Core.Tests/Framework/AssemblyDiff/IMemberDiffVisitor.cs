using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public interface IMemberDiffVisitor<T> where T : IMemberDefinition
    {
        bool VisitType(T source, T target);
        
        bool VisitAttributes(T source, T target);
        
        bool VisitAccessors(T source, T target);

        bool VisitCustomAttributes(T source, T target);
    }

    public interface IMemberDiffVisitor
    {
        bool VisitMissing(IMemberDefinition member, TypeDefinition target);

        bool VisitName(IMemberDefinition source, IMemberDefinition target);

        bool VisitDeclaringType(IMemberDefinition source, IMemberDefinition target);

        bool VisitCustomAttributes(IMemberDefinition source, IMemberDefinition target);
    }
}
