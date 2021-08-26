using Cecilifier.Core.Misc;

namespace Cecilifier.Core.TypeSystem
{
    internal sealed class Bcl
    {
        public Bcl(ITypeResolver typeResolver, CecilifierContext cecilifierContext)
        {
            System = new(typeResolver, cecilifierContext);
        }

        public SystemTypeSystem System { get; }
    }
}
