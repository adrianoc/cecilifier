using System;
using Cecilifier.Core.AST;

namespace Cecilifier.Core.Misc
{
    internal struct ContextFlagReseter : IDisposable
    {
        private readonly IVisitorContext _context;
        private readonly string _flagName;

        public ContextFlagReseter(IVisitorContext context, string flagName)
        {
            _context = context;
            _flagName = flagName;
            _context.SetFlag(flagName);
        }

        public void Dispose()
        {
            if (_context != null)
                _context.ClearFlag(_flagName);
        }
    }
}
