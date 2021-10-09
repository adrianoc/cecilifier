namespace Cecilifier.Core.Mappings
{
    public class LineInformation
    {
        public int Line { get; set; }
        public int Column { get; set; }

        public override string ToString()
        {
            return $"({Line}, {Column})";
        }
    }
}
