using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.ApiDriver;
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

    LinkedListNode<string> CurrentLine { get; }
    int CecilifiedLineNumber { get; }

    void OnFinishedTypeDeclaration();

    IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
    ITypeSymbol GetDeclaredSymbol(BaseTypeDeclarationSyntax classDeclaration);
    TypeInfo GetTypeInfo(TypeSyntax node);
    TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);

    //TODO: Move IL related methods to IILGeneratorApiDriver
    //      Do we need to move all of them? Maybe introduce a base type to extract common functionality 
    //      among the ApiDrivers
    void EmitCilInstruction(string ilVar, OpCode opCode);
    void EmitCilInstruction<T>(string ilVar, OpCode opCode, T operand, string comment = null);
    void WriteCilInstructionAfter(string ilVar, OpCode opCode, LinkedListNode<string> after);
    
    void Generate(CecilifierInterpolatedStringHandler expression);
    void Generate(string expression);
    void Generate(IEnumerable<string> expressions);
    void WriteComment(string comment);
    void WriteNewLine();
    void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);
    void MoveLinesToEnd(LinkedListNode<string> start, LinkedListNode<string> end);

    ITypeResolver TypeResolver { get; }
    IMethodResolver MethodResolver { get; }
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

public class IlContext
{
    protected IlContext(string variableName, string relatedMethodVar) => (VariableName, RelatedMethodVariable) = (variableName, relatedMethodVar);
    public static readonly IlContext None = new IlContext(string.Empty, string.Empty);
    
    //TODO: Remove these implicit operators and fix all usages of il variable names
    public static implicit operator IlContext(string variableName) => new(variableName, "N/A");
    public static implicit operator string(IlContext x) => x.VariableName;
    public virtual string VariableName { get; }
    public string RelatedMethodVariable { get; }
}
