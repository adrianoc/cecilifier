using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilTypeResolver : TypeResolverImpl
{
    public MonoCecilTypeResolver(IVisitorContext context) : base(context)
    {
    }
}
