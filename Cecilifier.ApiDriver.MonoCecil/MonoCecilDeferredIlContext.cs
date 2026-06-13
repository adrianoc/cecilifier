using System.Diagnostics;
using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilDeferredIlContext : IlContext
{
    private readonly IVisitorContext _context;
    private bool _emitted;
#if DEBUG_MATERIALIZATION_CHECK
    private string _debugInfo;        
#endif

    protected internal MonoCecilDeferredIlContext(IVisitorContext context, string ilVarName, string relatedMethodVar) : base(ilVarName, relatedMethodVar)
    {
        _context = context;
        _emitted = false;
        
#if DEBUG_MATERIALIZATION_CHECK
        _debugInfo = new StackTrace(true).ToString();
#endif
    }

    public override string VariableName
    {
        get
        {
#if DEBUG_MATERIALIZATION_CHECK
            if (!_emitted)
                throw new InvalidOperationException($"IL context is not materialized yet\nIlContext instantiated at:\n{_debugInfo}\n----------------------------");
#else
            Materialize();
#endif
            
            return base.VariableName;
        }
    }

    public override void Materialize()
    {
        if (!_emitted)
        {
            _emitted = true;
            _context.Generate($"var {base.VariableName} = {AssociatedMethodVariable}.Body.GetILProcessor();");
            _context.WriteNewLine();
        }
    }
}
