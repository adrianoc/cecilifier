using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilDeferredIlContext : IlContext
{
    private readonly IVisitorContext _context;
    private bool _emitted;

    protected internal MonoCecilDeferredIlContext(IVisitorContext context, string ilVarName, string relatedMethodVar) : base(ilVarName, relatedMethodVar)
    {
        _context = context;
        _emitted = false;
    }

    public override string VariableName
    {
        get
        {
            if (!_emitted)
            {
                _emitted = true;
                _context.Generate($"var {base.VariableName} = {RelatedMethodVariable}.Body.GetILProcessor();");
                _context.WriteNewLine();
            }
            
            return base.VariableName;
        }
    }
}
