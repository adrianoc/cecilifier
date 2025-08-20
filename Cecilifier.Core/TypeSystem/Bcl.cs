using Cecilifier.Core.AST;

namespace Cecilifier.Core.TypeSystem
{
    public sealed class Bcl
    {
        public Bcl(ITypeResolver typeResolver, IVisitorContext cecilifierContext)
        {
            System = new SystemTypeSystem(typeResolver, cecilifierContext);
        }

        public SystemTypeSystem System { get; }
    }
}
