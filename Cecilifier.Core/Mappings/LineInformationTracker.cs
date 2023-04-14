using System;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Mappings
{
    internal struct LineInformationTracker : IDisposable
    {
        private readonly IVisitorContext _context;
        private readonly SyntaxNode _node;
        private Mapping _current;

        private LineInformationTracker(IVisitorContext context, SyntaxNode node)
        {
            _context = context;
            _node = node;
            _current = new Mapping();

            BeginSourceElement();
        }

        public void Dispose()
        {
            EndSourceElement();
        }

        public void Discard()
        {
            _context.Mappings.Remove(_current);
            _current = null;
        }

        internal static LineInformationTracker Track(IVisitorContext context, SyntaxNode node) => new LineInformationTracker(context, node);

        private void BeginSourceElement()
        {
            var sourceStart = _node.GetLocation().GetLineSpan();
#if DEBUG
            _current.Node = _node;
#endif

            _current.Source.Begin.Line = sourceStart.StartLinePosition.Line + 1;
            _current.Source.Begin.Column = sourceStart.StartLinePosition.Character + 1;
            _current.Source.End.Line = sourceStart.EndLinePosition.Line + 1;
            _current.Source.End.Column = sourceStart.EndLinePosition.Character + 1;

            _current.Cecilified.Begin.Line = _context.CecilifiedLineNumber;
            _context.Mappings.Add(_current);
        }

        private void EndSourceElement()
        {
            if (_current != null)
                _current.Cecilified.End.Line = _context.CecilifiedLineNumber;
        }
    }
}
