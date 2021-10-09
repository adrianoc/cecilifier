namespace Cecilifier.Core.Mappings
{
    public class Mapping
    {
        public MappingBlock Source { get; } = new();
        public MappingBlock Cecilified { get; } = new();

        public override string ToString()
        {
            return $"{Source} <-> {Cecilified}";
        }
    }
}
