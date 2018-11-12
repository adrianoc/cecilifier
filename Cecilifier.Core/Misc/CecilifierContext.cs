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
		}

	    public SemanticModel SemanticModel
	    {
            get { return semanticModel; }
	    }

		public IMethodParameterContext Parameters { get; set; }

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

		public IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration)
		{
			return (IMethodSymbol) semanticModel.GetDeclaredSymbol(methodDeclaration);
		}

		public ITypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration)
		{
			return (ITypeSymbol) semanticModel.GetDeclaredSymbol(classDeclaration);
		}

		public Microsoft.CodeAnalysis.TypeInfo GetTypeInfo(TypeSyntax node)
		{
			return semanticModel.GetTypeInfo(node);
		}

        public Microsoft.CodeAnalysis.TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax)
        {
            return semanticModel.GetTypeInfo(expressionSyntax);
        }

		public INamedTypeSymbol GetSpecialType(SpecialType specialType)
		{
			return semanticModel.Compilation.GetSpecialType(specialType);
		}

		public void WriteCecilExpression(string msg, params object[] args)
		{
			var expression = string.Format(msg, args);
		    output.AddLast(expression);
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

		public void RegisterTypeLocalVariable(TypeDeclarationSyntax node, string varName)
		{
			typeToTypeInfo[node] = new TypeInfo(varName);
		}

		public string ResolveTypeLocalVariable(string typeName)
		{
			var typeDeclaration = typeToTypeInfo.Keys.OfType<TypeDeclarationSyntax>().Where(td => td.Identifier.ValueText == typeName).SingleOrDefault();
			return typeDeclaration != null ? typeToTypeInfo[typeDeclaration].LocalVariable : null;
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

	    public void EnterScope()
	    {
	        scopes.Add(new Dictionary<string, string>());
	    }

	    public void LeaveScope()
	    {
	        scopes.RemoveAt(scopes.Count - 1);
	    }

	    public void AddLocalVariableMapping(string variableName, string cecilVarDeclName)
	    {
	        scopes[scopes.Count - 1].Add(variableName, cecilVarDeclName);
	    }

	    public string MapLocalVariableNameToCecil(string localVariableName)
	    {
	        for (int i = scopes.Count - 1; i >= 0; i--)
	        {
	            if (scopes[i].TryGetValue(localVariableName, out var found))
	            {
	                return found;
	            }
	        }

	        return null;
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

		private Stack<LocalVariable> nodeStack = new Stack<LocalVariable>();
		private IList<IDictionary<string, string>> scopes = new List<IDictionary<string, string>>();
		protected IDictionary<BaseTypeDeclarationSyntax, TypeInfo> typeToTypeInfo = new Dictionary<BaseTypeDeclarationSyntax, TypeInfo>();
		private string @namespace;
	    private IDictionary<string, string> vars =new Dictionary<string, string>();
	}
}
