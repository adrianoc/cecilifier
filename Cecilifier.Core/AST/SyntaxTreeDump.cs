using System;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
    class SyntaxTreeDump : SyntaxWalker
    {
        public SyntaxTreeDump(string msg, SyntaxNode node)
        {
            Console.WriteLine(msg);
            Visit(node);
        }

        public override void Visit(SyntaxNode node)
        {
            Ident(ident, level => 
            {
                ident = level;
                Console.WriteLine("{2}[{0}] : {1}", node.GetType().Name, node, level);
                base.Visit(node);
            });
        }

        private void Ident(string level, Action<string> action)
        {
            action(ident + "\t");
            ident = level;
        }

        private string ident = "";
    }
}
