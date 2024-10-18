using Cecilifier.Core.Misc;

namespace Cecilifier.Core.TypeSystem
{
    public sealed class Bcl
    {
        public Bcl(ITypeResolver typeResolver, CecilifierContext cecilifierContext)
        {
            System = new SystemTypeSystem(typeResolver, cecilifierContext);
        }

        public SystemTypeSystem System { get; }
    }
}
