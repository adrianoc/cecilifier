using System;
using System.Collections.Generic;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeInfo = Microsoft.CodeAnalysis.TypeInfo;

namespace Cecilifier.Core.AST
{
	internal interface IMethodParameterContext
	{
		string Register(string paramName);
		string BackingVariableNameFor(string name);
	}

	interface IVisitorContext
	{
		string Output { get; }
		
		string Namespace { get; set; }
	    SemanticModel SemanticModel { get; }

		IMethodParameterContext Parameters { get; set; }
		LinkedListNode<string> CurrentLine { get; }
		
		IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
		ITypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration);
		TypeInfo GetTypeInfo(TypeSyntax node);
        TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);
		INamedTypeSymbol GetSpecialType(SpecialType specialType);
		
		void WriteCecilExpression(string msg);
		
		void PushLocalVariable(LocalVariable localVariable);
		LocalVariable PopLocalVariable();
		LocalVariable CurrentLocalVariable { get; }

		int NextFieldId();
		int NextLocalVariableTypeId();
		void RegisterTypeLocalVariable(string typeName, string varName);
		string ResolveTypeLocalVariable(string typeName);
	    string this[string name] { get; set; }
		bool Contains(string name);
	    void Remove(string varName);
		void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);

	    void EnterScope();
	    void LeaveScope();
	    void AddLocalVariableMapping(string variableName, string cecilVarDeclName);
	    string MapLocalVariableNameToCecil(string localVariableName);

		TypeScope BeginType(string typeName);
		string CurrentType { get; }
	}

	struct TypeScope : IDisposable
	{
		private readonly Stack<string> _typeDefStack;

		public TypeScope(Stack<string> typeDefStack)
		{
			_typeDefStack = typeDefStack;
		}

		public void Dispose()
		{
			_typeDefStack.Pop();
		}
	}
}
