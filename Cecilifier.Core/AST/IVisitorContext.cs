using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.DefinitionsFactory;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Services;
using Cecilifier.Core.Variables;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.AST;

public interface IVisitorContext
{
    static virtual IVisitorContext CreateContext(CecilifierOptions options, SemanticModel semanticModel) => throw new NotImplementedException();
    static virtual string[] BclAssembliesForCompilation() => throw new NotImplementedException();
    static virtual string[] GetPreprocessorSymbols() => throw new NotImplementedException();

    IApiDriverDefinitionsFactory ApiDefinitionsFactory { get; }
    public IILGeneratorApiDriver ApiDriver { get; }
        
    public int Indentation { get; }
    public string Output { get; }
    public IList<CecilifierDiagnostic> Diagnostics { get; }

    ServiceCollection Services { get; }
        
    void EmitWarning(string message, SyntaxNode node = null);
        
    void EmitError(string message, SyntaxNode node = null);
        
    INameStrategy Naming { get; }

    SemanticModel SemanticModel { get; }

    DefinitionVariableManager DefinitionVariables { get; }
    DefinitionVariable GetMethodVariable(IMethodSymbol method);

    LinkedListNode<string> CurrentLine { get; }
    int CecilifiedLineNumber { get; }

    void OnFinishedTypeDeclaration(INamedTypeSymbol typeSymbol);

    IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
    ITypeSymbol GetDeclaredSymbol(BaseTypeDeclarationSyntax classDeclaration);
    TypeInfo GetTypeInfo(TypeSyntax node);
    TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);

    void WriteCilInstructionAfter(IlContext ilVar, OpCode opCode, LinkedListNode<string> after);
    void Generate(CecilifierInterpolatedStringHandler expression);
    void Generate(string expression);
    void Generate(IEnumerable<string> expressions);
    void WriteComment(string comment);
    void WriteNewLine();
    void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);
    void MoveLinesToEnd(LinkedListNode<string> start, LinkedListNode<string> end);

    ITypeResolver TypeResolver { get; }
    IMemberResolver MemberResolver { get; }
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
