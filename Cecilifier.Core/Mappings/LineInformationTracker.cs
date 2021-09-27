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
            
        internal static IDisposable Track(IVisitorContext context, SyntaxNode node) => new LineInformationTracker(context, node);
            
        private void BeginSourceElement()
        {
            var sourceStart = _node.GetLocation().GetLineSpan();
            
            _current.Source.Begin.Line = sourceStart.StartLinePosition.Line;
            _current.Source.Begin.Column = sourceStart.StartLinePosition.Character;

            _current.Cecilified.Begin.Line = _context.LineNumber;
        }
        
        private void EndSourceElement()
        {
            var sourceStart = _node.GetLocation().GetLineSpan();
            
            _current.Source.End.Line = sourceStart.EndLinePosition.Line;
            _current.Source.End.Column = sourceStart.EndLinePosition.Character;

            _current.Cecilified.End.Line = _context.LineNumber;
            
            _context.Mappings.Add(_current);
        }
    }
}
