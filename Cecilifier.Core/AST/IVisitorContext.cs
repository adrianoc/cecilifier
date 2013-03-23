using System.Collections.Generic;
using Cecilifier.Core.Misc;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;
using TypeInfo = Roslyn.Compilers.CSharp.TypeInfo;

namespace Cecilifier.Core.AST
{
	interface IVisitorContext
	{
		string Output { get; }
		
		string Namespace { get; set; }
	    SemanticModel SemanticModel { get; }

		LocalVariable CurrentLocalVariable { get; }
		LinkedListNode<string> CurrentLine { get; }
		MethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
		TypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration);
		TypeInfo GetTypeInfo(TypeSyntax node);
        TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);
		NamedTypeSymbol GetSpecialType(SpecialType specialType);
		
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
	}
}
