using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public interface IEventDiffVisitor : IMemberDiffVisitor<EventDefinition>
    {
        EventDefinition VisitEvent(EventDefinition sourceEvent, TypeDefinition target);
    }
}
