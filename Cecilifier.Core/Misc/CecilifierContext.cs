using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Services;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Misc
{
    public class CecilifierContext : IVisitorContext
    {
        private readonly IDictionary<string, string> flags = new Dictionary<string, string>();
        private readonly LinkedList<string> output = new();

        private readonly string identation;
        private int startLineNumber;
        private RoslynTypeSystem roslynTypeSystem;

        public CecilifierContext(SemanticModel semanticModel, CecilifierOptions options, byte indentation = 3)
        {
            SemanticModel = semanticModel;
            Options = options;
            DefinitionVariables = new DefinitionVariableManager();
            roslynTypeSystem = new RoslynTypeSystem(this);
            TypeResolver = new TypeResolverImpl(this);
            Mappings = new List<Mapping>();
            Diagnostics = [];
            CecilifiedLineNumber = options.GeneratorApiDriver.PreambleLineCount;
            startLineNumber = options.GeneratorApiDriver.PreambleLineCount;
            ApiDefinitionsFactory = Options.GeneratorApiDriver.CreateDefinitionsFactory();

            identation = new String('\t', indentation);
            
            Services.Add(new GenericInstanceMethodCacheService<int, string>());
        }

        public IApiDriverDefinitionsFactory ApiDefinitionsFactory { get; }
        
        public ServiceCollection Services { get; } = new();

        public string Output
        {
            get { return output.Aggregate("", (acc, curr) => acc + curr); }
        }

        public ITypeResolver TypeResolver { get; }
        public ref readonly RoslynTypeSystem RoslynTypeSystem => ref roslynTypeSystem;
        public SemanticModel SemanticModel { get; }
        public CecilifierOptions Options { get; }
        public INameStrategy Naming => Options.Naming;

        public DefinitionVariableManager DefinitionVariables { get; }

        public LinkedListNode<string> CurrentLine => output.Last;

        public int CecilifiedLineNumber { get; private set; }

        public IList<Mapping> Mappings { get; }

        public IList<CecilifierDiagnostic> Diagnostics { get; }

        public void EmitWarning(string message, SyntaxNode node = null) => EmitDiagnostic(message, node, DiagnosticKind.Warning); 
        public void EmitError(string message, SyntaxNode node = null) => EmitDiagnostic(message, node, DiagnosticKind.Error);
        
        private void EmitDiagnostic(string message, SyntaxNode node, DiagnosticKind diagnosticKind)
        {
            Diagnostics.Add(CecilifierDiagnostic.FromAstNode(node, diagnosticKind, message));
            
            var diagnosticKindString = diagnosticKind == DiagnosticKind.Warning ? "warning" : "error";
            var lines = message.Split('\n');
            foreach (var line in lines)
            {
                WriteCecilExpression($"#{diagnosticKindString} {line}");
                WriteNewLine();
            }
        }
        
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

        public void WriteCecilExpression(string expression)
        {
            CecilifiedLineNumber += expression.CountNewLines();
            output.AddLast($"{identation}{expression}");
        }
        
        public void WriteCecilExpressions(IEnumerable<string> expressions)
        {
            foreach (var expression in expressions.Where(exp => !string.IsNullOrWhiteSpace(exp)))
            {
                WriteCecilExpression(expression);
                WriteNewLine();
            }
        }

        public void WriteComment(string comment)
        {
            if ((Options.Naming.Options & NamingOptions.AddCommentsToMemberDeclarations) == NamingOptions.AddCommentsToMemberDeclarations)
            {
                output.AddLast($"{identation}//{comment}");
                CecilifiedLineNumber += comment.CountNewLines();
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

        public void WriteCilInstructionAfter<T>(string ilVar, OpCode opCode, T operand, string comment, LinkedListNode<string> after)
        {
            var operandStr = operand == null ? string.Empty : $", {operand}";
            var toBeWritten = $"{ilVar}.Emit({opCode.ConstantName()}{operandStr});{(comment != null ? $" // {comment}" : string.Empty)}\n";
            
            output.AddAfter(after, $"{identation}{toBeWritten}");
            CecilifiedLineNumber += toBeWritten.CountNewLines();
        }
        
        public void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after)
        {
            if (instruction == after)
                return;

            var lineToBeMoved = LineOf(instruction) + 1;
            var lineToMoveAfter = LineOf(after.Next);

            output.Remove(instruction);
            output.AddAfter(after, instruction);

            var numberOfLinesBeingMoved = instruction.Value.CountNewLines();

            foreach (var b in Mappings)
            {
                if (b.Cecilified.Begin.Line >= lineToBeMoved)
                {
                    b.Cecilified.Begin.Line -= numberOfLinesBeingMoved;
                    if (b.Cecilified.End.Line <= lineToMoveAfter)
                        b.Cecilified.End.Line -= numberOfLinesBeingMoved;
                }
                else if (b.Cecilified.End.Line >= lineToBeMoved)
                {
                    b.Cecilified.End.Line -= numberOfLinesBeingMoved;
                }
            }

            int LineOf(LinkedListNode<string> linkedListNode)
            {
                var lineUntilPassedNode = 0;
                var f = output.First;
                while (f != linkedListNode && f != null)
                {
                    lineUntilPassedNode += f.Value.CountNewLines();
                    f = f.Next;
                }

                return lineUntilPassedNode + startLineNumber;
            }
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

        public T WithFlag<T>(string name) where T : struct, IDisposable 
        {
            return (T)(IDisposable) new ContextFlagReseter(this, name);
        }

        public bool HasFlag(string name)
        {
            return flags.ContainsKey(name);
        }
        
        public bool TryGetFlag(string name, out string value)
        {
            return flags.TryGetValue(name, out value);
        }
        
        public void SetFlag(string name, string value = null)
        {
            flags[name] = value;
        }

        public void ClearFlag(string name)
        {
            flags.Remove(name);
        }        

        public void EmitCilInstruction<T>(string ilVar, OpCode opCode, T operand, string comment = null)
        {
            var operandStr = operand == null ? string.Empty : $", {operand}";
            WriteCecilExpression($"{ilVar}.Emit({opCode.ConstantName()}{operandStr});{(comment != null ? $" // {comment}" : string.Empty)}");
            WriteNewLine();
        }

        public void EmitCilInstruction(string ilVar, OpCode opCode)
        {
            EmitCilInstruction<string>(ilVar, opCode, null);
        }
    }
}
