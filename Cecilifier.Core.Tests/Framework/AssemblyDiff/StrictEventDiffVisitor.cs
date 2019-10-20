using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class StrictEventDiffVisitor : IEventDiffVisitor
    {
        private readonly TextWriter _output;

        public StrictEventDiffVisitor(TextWriter output)
        {
            _output = output;
        }

        public EventDefinition VisitEvent(EventDefinition sourceEvent, TypeDefinition target)
        {
            var @event = target.Events.SingleOrDefault(evt => evt.FullName == sourceEvent.FullName);
            if (@event == null)
            {
                _output.WriteLine($"Event '{sourceEvent.FullName}' not found in type '{target.FullName}'");
            }
            return @event;
        }

        public bool VisitType(EventDefinition source, EventDefinition target)
        {
            var ret = source.EventType.FullName == target.EventType.FullName;
            if (!ret)
                _output.WriteLine($"Types of event '{source.Name}' differs. Expected '{source.EventType.FullName}' but got '{target.EventType.FullName}'");
                
            return ret;
        }

        public bool VisitAttributes(EventDefinition source, EventDefinition target)
        {
            return true;
        }

        public bool VisitAccessors(EventDefinition source, EventDefinition target)
        {
            var ret = source.Attributes == target.Attributes;
            if (!ret)
                _output.WriteLine($"Attributes of event '{source.Name}' differs. Expected '{source.Attributes}' but got '{target.Attributes}'");
            
            return ret;
        }
    }
}
