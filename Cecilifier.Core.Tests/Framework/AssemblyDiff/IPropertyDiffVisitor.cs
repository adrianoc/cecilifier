using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public interface IPropertyDiffVisitor : IMemberDiffVisitor<PropertyDefinition>
    {
        PropertyDefinition VisitProperty(PropertyDefinition property, TypeDefinition target);
    }
}
