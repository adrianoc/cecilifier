using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Misc
{
    internal class CecilifierContext : IVisitorContext
    {
        private readonly LinkedList<string> output = new LinkedList<string>();

        private int currentFieldId;

        private int currentTypeId;

        private readonly Dictionary<string, string> vars = new Dictionary<string, string>();

        public CecilifierContext(SemanticModel semanticModel)
        {
            SemanticModel = semanticModel;
            DefinitionVariables = new DefinitionVariableManager();
        }

        public string Output
        {
            get { return output.Aggregate("", (acc, curr) => acc + curr); }
        }

        public SemanticModel SemanticModel { get; }

        public DefinitionVariableManager DefinitionVariables { get; } = new DefinitionVariableManager();

        public string Namespace { get; set; }

        public LinkedListNode<string> CurrentLine => output.Last;

        public IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration)
        {
            return (IMethodSymbol) SemanticModel.GetDeclaredSymbol(methodDeclaration);
        }

        public ITypeSymbol GetDeclaredSymbol(TypeDeclarationSyntax classDeclaration)
        {
            return (ITypeSymbol) SemanticModel.GetDeclaredSymbol(classDeclaration);
        }

        public TypeInfo GetTypeInfo(TypeSyntax node)
        {
            return SemanticModel.GetTypeInfo(node);
        }

        public TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax)
        {
            return SemanticModel.GetTypeInfo(expressionSyntax);
        }

        public INamedTypeSymbol GetSpecialType(SpecialType specialType)
        {
            return SemanticModel.Compilation.GetSpecialType(specialType);
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
            get => vars[name];
            set => vars[name] = value;
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
    }
}
