using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilTypeResolver : TypeResolverBase
{
    public MonoCecilTypeResolver(IVisitorContext context) : base(context)
    {
    }
}
