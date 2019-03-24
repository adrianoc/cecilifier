using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Misc
{
	class CecilifierContext : IVisitorContext
	{
		public CecilifierContext(SemanticModel semanticModel)
		{
			this.semanticModel = semanticModel;
			DefinitionVariables = new DefinitionVariableManager();
		}

	    public SemanticModel SemanticModel
	    {
            get { return semanticModel; }
	    }

		public DefinitionVariableManager DefinitionVariables { get; } = new DefinitionVariableManager();

		public string Namespace
		{
			get { return @namespace; }
			set { @namespace = value; }
		}

		public LinkedListNode<string> CurrentLine
		{
			get { return output.Last; }
		}

		public IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration)
		{
			return (IMethodSymbol) semanticModel.GetDeclaredSymbol(methodDeclaration);
		}

		public ITypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration)
		{
			return (ITypeSymbol) semanticModel.GetDeclaredSymbol(classDeclaration);
		}

		public TypeInfo GetTypeInfo(TypeSyntax node)
		{
			return semanticModel.GetTypeInfo(node);
		}

        public TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax)
        {
            return semanticModel.GetTypeInfo(expressionSyntax);
        }

		public INamedTypeSymbol GetSpecialType(SpecialType specialType)
		{
			return semanticModel.Compilation.GetSpecialType(specialType);
		}

		public void WriteCecilExpression(string expression)
		{
		    output.AddLast("\t\t" + expression);
		}

		public int NextFieldId()
		{
			return ++currentFieldId;
		}

		public int NextLocalVariableTypeId()
		{
			return ++currentTypeId;
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

		public void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after)
		{
			output.AddAfter(after, instruction.Value);
			output.Remove(instruction);
		}

		public event Action<string> InstructionAdded;

		public void TriggerInstructionAdded(string instVar)
		{
			InstructionAdded?.Invoke(instVar);	
		}

		public string Output
		{
			get
			{
				return output.Aggregate("", (acc, curr) => acc + curr);
			}
		}

		private readonly SemanticModel semanticModel;
		private readonly LinkedList<string> output = new LinkedList<string>();
		
		private int currentTypeId;
		private int currentFieldId;

		private string @namespace;
	    private Dictionary<string, string> vars =new Dictionary<string, string>();
	}
}
