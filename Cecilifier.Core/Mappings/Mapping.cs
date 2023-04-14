using Cecilifier.Core.Extensions;

namespace Cecilifier.Core.Mappings
{
    public class Mapping
    {
        public MappingBlock Source { get; } = new();
        public MappingBlock Cecilified { get; } = new();

#if DEBUG
        public Microsoft.CodeAnalysis.SyntaxNode Node;
#endif

        public override string ToString()
        {
#if DEBUG
            return $"[{Node.HumanReadableSummary()}]  {Source} <-> {Cecilified}";
#else            
            return $"{Source} <-> {Cecilified}";
#endif
        }
    }
}
