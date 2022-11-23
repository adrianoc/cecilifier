using System;
using System.Collections.Generic;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal interface IVisitorContext
    {
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
        void WriteCecilExpression(string msg);
        void WriteComment(string comment);
        void WriteNewLine();

        void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);
        void MoveLinesToEnd(LinkedListNode<string> start, LinkedListNode<string> end);
        
        ITypeResolver TypeResolver { get; }
        IList<Mapping> Mappings { get; }
        
        ref readonly RoslynTypeSystem RoslynTypeSystem { get; }

        #region Flags Handling
        IDisposable WithFlag(string name);
        bool HasFlag(string name);

        #endregion

    }
}
