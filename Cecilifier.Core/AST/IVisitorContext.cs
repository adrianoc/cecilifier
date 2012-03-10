using System;
using Cecilifier.Core.Misc;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	interface IVisitorContext
	{
		string Namespace { get; set; }
		string Output { get; }
		LocalVariable CurrentLocalVariable { get; }
		MethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
		TypeSymbol GetDeclaredSymbol(ClassDeclarationSyntax classDeclaration);
		SemanticInfo GetSemanticInfo(TypeSyntax node);
		NamedTypeSymbol GetSpecialType(SpecialType specialType);
		
		void WriteCecilExpression(string msg, params object[] args);
		
		void PushLocalVariable(LocalVariable localVariable);
		LocalVariable PopLocalVariable();

		int NextFieldId();
		int NextLocalVariableTypeId();
		void RegisterTypeLocalVariable(TypeDeclarationSyntax node, string varName, Action<string, BaseTypeDeclarationSyntax> ctorInjector);
		string ResolveLocalVariable(string typeName);
		void SetDefaultCtorInjectorFor(BaseTypeDeclarationSyntax type, Action<string, BaseTypeDeclarationSyntax> ctorInjector);
		void EnsureCtorDefinedForCurrentType();
	}
}
