using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Misc
{
	class CecilifierContext : IVisitorContext
	{
		public CecilifierContext(SemanticModel semanticModel)
		{
			this.semanticModel = semanticModel;
		}

	    public SemanticModel SemanticModel
	    {
            get { return semanticModel; }
	    }

		public string Namespace
		{
			get { return @namespace; }
			set { @namespace = value; }
		}

		public LocalVariable CurrentLocalVariable
		{
			get { return nodeStack.Peek(); }
		}

		public LinkedListNode<string> CurrentLine
		{
			get { return output.Last; }
		}

		public MethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration)
		{
			return (MethodSymbol) semanticModel.GetDeclaredSymbol(methodDeclaration);
		}

		public TypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration)
		{
            return semanticModel.GetDeclaredSymbol(classDeclaration);
		}

		public SemanticInfo GetSemanticInfo(TypeSyntax node)
		{
			return semanticModel.GetSemanticInfo(node);
		}

        public SemanticInfo GetSemanticInfo(ExpressionSyntax expressionSyntax)
        {
            return semanticModel.GetSemanticInfo(expressionSyntax);
        }

		public NamedTypeSymbol GetSpecialType(SpecialType specialType)
		{
			return semanticModel.Compilation.GetSpecialType(specialType);
		}

		public void WriteCecilExpression(string msg, params object[] args)
		{
			output.AddLast(string.Format(msg, args));
		}

		public void PushLocalVariable(LocalVariable localVariable)
		{
			nodeStack.Push(localVariable);
		}

		public LocalVariable PopLocalVariable()
		{
			return nodeStack.Pop();
		}

		public int NextFieldId()
		{
			return ++currentFieldId;
		}

		public int NextLocalVariableTypeId()
		{
			return ++currentTypeId;
		}

		public void RegisterTypeLocalVariable(TypeDeclarationSyntax node, string varName, Action<string, BaseTypeDeclarationSyntax> ctorInjector)
		{
			typeToTypeInfo[node] = new TypeInfo(varName, ctorInjector);
		}

		public string ResolveTypeLocalVariable(string typeName)
		{
			var typeDeclaration = typeToTypeInfo.Keys.OfType<TypeDeclarationSyntax>().Where(td => td.Identifier.ValueText == typeName).SingleOrDefault();
			return typeDeclaration != null ? typeToTypeInfo[typeDeclaration].LocalVariable : null;
		}

		public void SetDefaultCtorInjectorFor(BaseTypeDeclarationSyntax type, Action<string, BaseTypeDeclarationSyntax> ctorInjector)
		{
			typeToTypeInfo[type].CtorInjector = ctorInjector;
		}

		public void EnsureCtorDefinedForCurrentType()
		{
			foreach (var pair in typeToTypeInfo)
			{
				pair.Value.CtorInjector(pair.Value.LocalVariable, pair.Key);
				pair.Value.CtorInjector = delegate { };
			}
		}

	    public string this[string name]
	    {
            get { return vars[name]; }
	        set { vars[name] = value; }
	    }

		public bool Contains(string name)
		{
			return vars.ContainsKey(name);
		}

		public void Remove(string varName)
        {
            vars.Remove(varName);
        }

		public void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after)
		{
			output.AddAfter(after, instruction.Value);
			output.Remove(instruction);
		}

		public string Output
		{
			get
			{
				EnsureCtorDefinedForCurrentType();
				return output.Aggregate("", (acc, curr) => acc + curr);
			}
		}

		private readonly SemanticModel semanticModel;
		private readonly LinkedList<string> output = new LinkedList<string>();
		
		private int currentTypeId;
		private int currentFieldId;

		private Stack<LocalVariable> nodeStack = new Stack<LocalVariable>();
		protected IDictionary<BaseTypeDeclarationSyntax, TypeInfo> typeToTypeInfo = new Dictionary<BaseTypeDeclarationSyntax, TypeInfo>();
		private string @namespace;
	    private IDictionary<string, string> vars =new Dictionary<string, string>();
	}
}
