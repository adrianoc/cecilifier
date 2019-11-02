using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public interface IMemberDiffVisitor
    {
        bool VisitMissing(IMemberDefinition member, TypeDefinition target);
        
        bool VisitName(IMemberDefinition source, IMemberDefinition target);
        
        bool VisitDeclaringType(IMemberDefinition source, IMemberDefinition target);
        
        bool VisitCustomAttributes(IMemberDefinition source, IMemberDefinition target);
    }
}
