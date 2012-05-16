using System;
using System.Collections.Generic;
using Cecilifier.Core.Misc;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	interface IVisitorContext
	{
		string Namespace { get; set; }
		string Output { get; }
	    SemanticModel SemanticModel { get; }

		LocalVariable CurrentLocalVariable { get; }
		LinkedListNode<string> CurrentLine { get; }
		MethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
		TypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration);
		SemanticInfo GetSemanticInfo(TypeSyntax node);
        SemanticInfo GetSemanticInfo(ExpressionSyntax expressionSyntax);
		NamedTypeSymbol GetSpecialType(SpecialType specialType);
		
		void WriteCecilExpression(string msg, params object[] args);
		
		void PushLocalVariable(LocalVariable localVariable);
		LocalVariable PopLocalVariable();

		int NextFieldId();
		int NextLocalVariableTypeId();
		void RegisterTypeLocalVariable(TypeDeclarationSyntax node, string varName, Action<string, BaseTypeDeclarationSyntax> ctorInjector);
		string ResolveTypeLocalVariable(string typeName);
		void SetDefaultCtorInjectorFor(BaseTypeDeclarationSyntax type, Action<string, BaseTypeDeclarationSyntax> ctorInjector);
		void EnsureCtorDefinedForCurrentType();
	    string this[string name] { get; set; }
		bool Contains(string name);
	    void Remove(string varName);
		void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);
	}
}
