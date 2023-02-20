using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core.AST
{
    internal class SyntaxTreeDump : CSharpSyntaxWalker
    {
        private string ident = "";

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
                Console.WriteLine("{2}[{0}/{3}] : {1}", node.GetType().Name, node, level, node.Kind());
                base.Visit(node);
            });
        }

        private void Ident(string level, Action<string> action)
        {
            action(ident + "\t");
            ident = level;
        }
    }
}
