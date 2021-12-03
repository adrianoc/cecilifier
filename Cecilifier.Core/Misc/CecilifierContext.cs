using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Misc
{
    internal class CecilifierContext : IVisitorContext
    {
        private readonly ISet<string> flags = new HashSet<string>();
        private readonly LinkedList<string> output = new();

        private string identation;

        public CecilifierContext(SemanticModel semanticModel, CecilifierOptions options,  int startingLine, byte indentation = 3)
        {
            SemanticModel = semanticModel;
            Options = options;
            DefinitionVariables = new DefinitionVariableManager();
            TypeResolver = new TypeResolverImpl(this);
            Mappings = new List<Mapping>();
            CecilifiedLineNumber = startingLine + 1; // always report as 1 based.
            
            this.identation = new String('\t', indentation);
        }

        public string Output
        {
            get { return output.Aggregate("", (acc, curr) => acc + curr); }
        }

        public ITypeResolver TypeResolver { get; }

        public SemanticModel SemanticModel { get; }
        public CecilifierOptions Options { get; }
        public INameStrategy Naming => Options.Naming;

        public DefinitionVariableManager DefinitionVariables { get; }

        public string CurrentNamespace { get; set; }

        public LinkedListNode<string> CurrentLine => output.Last;

        public int CecilifiedLineNumber { get; private set; }
        
        public IList<Mapping> Mappings { get; }

        public IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration)
        {
            return (IMethodSymbol) SemanticModel.GetDeclaredSymbol(methodDeclaration);
        }

        public ITypeSymbol GetDeclaredSymbol(BaseTypeDeclarationSyntax classDeclaration)
        {
            return (ITypeSymbol) SemanticModel.GetDeclaredSymbol(classDeclaration);
        }

        public TypeInfo GetTypeInfo(TypeSyntax node)
        {
            return SemanticModel.GetTypeInfo(node);
        }

        public TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax)
        {
            return SemanticModel.GetTypeInfo(expressionSyntax);
        }

        public INamedTypeSymbol GetSpecialType(SpecialType specialType)
        {
            return SemanticModel.Compilation.GetSpecialType(specialType);
        }

        public void WriteCecilExpression(string expression)
        {
            output.AddLast($"{identation}{expression}");
        }

        public void WriteComment(string comment)
        {
            if ((Options.Naming.Options & NamingOptions.AddCommentsToMemberDeclarations) == NamingOptions.AddCommentsToMemberDeclarations)
            {
                output.AddLast($"{identation}//{comment}");
                WriteNewLine();
            }
        }
        
        public void WriteNewLine()
        {
            if (output.Last == null)
            {
                output.AddLast(Environment.NewLine);
            }
            else
            {
                output.Last.Value = output.Last.Value + Environment.NewLine;    
            }
            CecilifiedLineNumber++;
        }

        public void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after)
        {
            output.AddAfter(after, instruction.Value);
            output.Remove(instruction);
        }

        public void MoveLinesToEnd(LinkedListNode<string> start, LinkedListNode<string> end)
        {
            // Counts the # of instructions to be moved...
            var c = start;
            int instCount = 0;
            while (c != end)
            {
                c = c!.Next;
                instCount++;
            }
            
            // move each line after the last one
            c = start.Next;
            while (instCount-- > 0)
            {
                var next = c!.Next;
                MoveLineAfter(c, CurrentLine);
                c = next;
            }
        }

        public IDisposable WithFlag(string name)
        {
            return new ContextFlagReseter(this, name);
        }

        public bool HasFlag(string name)
        {
            return flags.Contains(name);
        }

        internal void SetFlag(string name)
        {
            flags.Add(name);
        }
        
        internal void ClearFlag(string name)
        {
            flags.Remove(name);
        }
    }
}
