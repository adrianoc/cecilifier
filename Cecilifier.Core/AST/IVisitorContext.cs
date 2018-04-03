using System.Collections.Generic;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeInfo = Microsoft.CodeAnalysis.TypeInfo;

namespace Cecilifier.Core.AST
{
	interface IVisitorContext
	{
		string Output { get; }
		
		string Namespace { get; set; }
	    SemanticModel SemanticModel { get; }

		IMethodParameterContext Parameters { get; set; }
		LocalVariable CurrentLocalVariable { get; }
		LinkedListNode<string> CurrentLine { get; }
		IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
		ITypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration);
		TypeInfo GetTypeInfo(TypeSyntax node);
        TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);
		INamedTypeSymbol GetSpecialType(SpecialType specialType);
		
		void WriteCecilExpression(string msg, params object[] args);
		
		void PushLocalVariable(LocalVariable localVariable);
		LocalVariable PopLocalVariable();

		int NextFieldId();
		int NextLocalVariableTypeId();
		void RegisterTypeLocalVariable(TypeDeclarationSyntax node, string varName);
		string ResolveTypeLocalVariable(string typeName);
	    string this[string name] { get; set; }
		bool Contains(string name);
	    void Remove(string varName);
		void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);

	    void EnterScope();
	    void LeaveScope();
	    void AddLocalVariableMapping(string variableName, string cecilVarDeclName);
	    string MapLocalVariableNameToCecil(string localVariableName);
	}
}
