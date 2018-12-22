using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeInfo = Microsoft.CodeAnalysis.TypeInfo;

namespace Cecilifier.Core.AST
{
	
	public enum MemberKind
	{
		Type,
		Enum,
		Struct,
		Interface,
		Field,
		Property,
		Event,
		Method,
		Delegate,
		Parameter,
		LocalVariable
	}

	public struct DefinitionVariable : IEquatable<DefinitionVariable>
	{
		public string MemberName;
		public string ParentName;
		public MemberKind Kind;
		public string VariableName;
		public bool IsValid;

		public static DefinitionVariable NotFound;
		
		public override int GetHashCode()
		{
			unchecked
			{
				var hashCode = (MemberName != null ? MemberName.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (ParentName != null ? ParentName.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (int) Kind;
				return hashCode;
			}
		}

		public override string ToString()
		{
			if (ParentName == null)
				return $"(Kind = {Kind}) {MemberName}";
			
			return $"(Kind = {Kind}) {ParentName}.{MemberName}";
		}

		public bool Equals(DefinitionVariable other)
		{
			return string.Equals(MemberName, other.MemberName) && string.Equals(ParentName, other.ParentName) && Kind == other.Kind;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			return obj is DefinitionVariable other && Equals(other);
		}
	}

	public struct ScopedDefinitionVariable : IDisposable
	{
		private readonly List<DefinitionVariable> _definitionVariables;
		private readonly int _currentSize;

		public ScopedDefinitionVariable(List<DefinitionVariable> definitionVariables, int currentSize)
		{
			_definitionVariables = definitionVariables;
			_currentSize = currentSize;
		}

		public void Dispose()
		{
			if (_definitionVariables.Count > _currentSize)
			{
				_definitionVariables.RemoveRange(_currentSize, _definitionVariables.Count - _currentSize);
			}
		}
	}
	
	public class DefinitionVariableManager
	{
		public DefinitionVariable Register(string parentName, string memberName, MemberKind memberKind, string definitionVariableName)
		{
			var definitionVariable = new DefinitionVariable { ParentName = string.Empty, MemberName = memberName, Kind = memberKind, VariableName = definitionVariableName, IsValid = true };
			_definitionVariables.Add(definitionVariable);

			return definitionVariable;
		}
		
		public DefinitionVariable GetVariable(string memberName, MemberKind memberKind)
		{
			var tbf = new DefinitionVariable { ParentName = string.Empty, MemberName = memberName, Kind = memberKind };
			for(int i = _definitionVariables.Count - 1; i >= 0; i--)
				if (_definitionVariables[i].Equals(tbf))
					return _definitionVariables[i];
			
			return DefinitionVariable.NotFound;
		}

		public DefinitionVariable GetTypeVariable(string typeName)
		{
			return GetVariable(typeName, MemberKind.Type);
		}

		public DefinitionVariable GetLastOf(MemberKind kind)
		{
			var index = _definitionStack.FindLastIndex(c => c.Kind == kind);
			if (index == -1)
				return DefinitionVariable.NotFound;

			return _definitionStack[index];
		}

		public ScopedDefinitionVariable WithCurrent(string parentName, string memberName, MemberKind memberKind, string definitionVariableName)
		{
			var registered = Register(parentName, memberName, memberKind, definitionVariableName);
			_definitionStack.Add(registered);
			return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count - 1);
		}

		public ScopedDefinitionVariable EnterScope()
		{
			return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count);
		}
		
		public DefinitionVariable Current => _definitionVariables.Count > 0 ?  _definitionVariables[_definitionVariables.Count - 1] : DefinitionVariable.NotFound;

		private List<DefinitionVariable> _definitionVariables = new List<DefinitionVariable>();
		private List<DefinitionVariable> _definitionStack = new List<DefinitionVariable>();
	}

	interface IVisitorContext
	{
		string Output { get; }
		
		string Namespace { get; set; }
	    SemanticModel SemanticModel { get; }
		
		DefinitionVariableManager DefinitionVariables { get; }

		LinkedListNode<string> CurrentLine { get; }
		
		IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
		ITypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration);
		TypeInfo GetTypeInfo(TypeSyntax node);
        TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);
		INamedTypeSymbol GetSpecialType(SpecialType specialType);
		
		void WriteCecilExpression(string msg);
		
		int NextFieldId();
		int NextLocalVariableTypeId();
	    string this[string name] { get; set; }
		bool Contains(string name);
		void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);
	}
}
