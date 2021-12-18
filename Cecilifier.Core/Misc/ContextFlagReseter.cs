using System;

namespace Cecilifier.Core.Misc
{
    internal class ContextFlagReseter : IDisposable
    {
        private readonly CecilifierContext _context;
        private readonly string _flagName;

        public ContextFlagReseter(CecilifierContext context, string flagName)
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
