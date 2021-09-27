namespace Cecilifier.Core.Mappings
{
    public class MappingBlock
    {
        public LineInformation Begin { get; } = new();
        public LineInformation End { get; } = new();

        public int Length => End.Line - Begin.Line;

        public override string ToString()
        {
            return $"{{{Begin}, {End}}}";
        }
    }
}
