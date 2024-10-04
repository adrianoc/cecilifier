using System;
using System.Collections.Generic;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Services;
using Cecilifier.Core.Variables;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    public interface IVisitorContext
    {
        ServiceCollection Services { get; }
        
        void EmitWarning(string message, SyntaxNode node = null);
        
        void EmitError(string message, SyntaxNode node = null);
        
        INameStrategy Naming { get; }

        SemanticModel SemanticModel { get; }

        DefinitionVariableManager DefinitionVariables { get; }

        LinkedListNode<string> CurrentLine { get; }
        int CecilifiedLineNumber { get; }

        IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
        ITypeSymbol GetDeclaredSymbol(BaseTypeDeclarationSyntax classDeclaration);
        TypeInfo GetTypeInfo(TypeSyntax node);
        TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);

        void EmitCilInstruction(string ilVar, OpCode opCode);
        void EmitCilInstruction<T>(string ilVar, OpCode opCode, T operand, string comment = null);
        void WriteCecilExpression(string expression);
        void WriteCecilExpressions(IEnumerable<string> expressions);
        void WriteComment(string comment);
        void WriteNewLine();
        
        void WriteCilInstructionAfter<T>(string ilVar, OpCode opCode, T operand, string comment, LinkedListNode<string> after);
        void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);
        void MoveLinesToEnd(LinkedListNode<string> start, LinkedListNode<string> end);

        ITypeResolver TypeResolver { get; }
        IList<Mapping> Mappings { get; }

        ref readonly RoslynTypeSystem RoslynTypeSystem { get; }

        #region Flags Handling
        T WithFlag<T>(string name) where T : struct, IDisposable;
        bool HasFlag(string name);

        void SetFlag(string name, string value = null);
        bool TryGetFlag(string name, out string value);
        void ClearFlag(string name);

        #endregion

    }
}
