using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
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
            TypeResolver = new TypeResolverImpl(this);
        }

        public string Output
        {
            get { return output.Aggregate("", (acc, curr) => acc + curr); }
        }

        public ITypeResolver TypeResolver { get; }

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

    internal class TypeResolverImpl : ITypeResolver
    {
        private readonly CecilifierContext _context;

        public TypeResolverImpl(CecilifierContext context)
        {
            _context = context;
        }

        public string Resolve(ITypeSymbol type)
        {
            return ResolveTypeLocalVariable(type.Name)
                   ?? ResolvePredefinedAndComposedTypes(type)
                   ?? ResolveGenericType(type)
                   ?? Resolve(type.Name);
        }

        public string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");
        
        public string ResolvePredefinedType(string typeName) => "assembly.MainModule.TypeSystem." + typeName;

        public string ResolvePredefinedType(ITypeSymbol type) => ResolvePredefinedType(type.Name);

        public string ResolvePredefinedAndComposedTypes(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.None || type.TypeKind == TypeKind.Interface || type.SpecialType == SpecialType.System_Enum)
            {
                return null;
            }

            if (type.SpecialType == SpecialType.System_Array)
            {
                var ats = (IArrayTypeSymbol) type;
                return "new ArrayType(" + Resolve(ats.ElementType) + ")";
            }

            return ResolvePredefinedType(type.Name);
        }

        public string ResolveGenericType(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol genericTypeSymbol) || !genericTypeSymbol.IsGenericType)
            {
                return null;
            }

            var genericType = Resolve(OpenGenericTypeName(genericTypeSymbol.ConstructedFrom));
            var args = string.Join(",", genericTypeSymbol.TypeArguments.Select(a => Resolve(a)));
            return $"{genericType}.MakeGenericInstanceType({args})";
        }

        public string ResolveTypeLocalVariable(string typeName) => _context.DefinitionVariables.GetVariable(typeName, MemberKind.Type).VariableName;

        private string OpenGenericTypeName(ITypeSymbol type)
        {
            var genericTypeWithTypeParameters = type.ToString();

            var genOpenBraceIndex = genericTypeWithTypeParameters.IndexOf('<');
            var genCloseBraceIndex = genericTypeWithTypeParameters.LastIndexOf('>');

            var nts = (INamedTypeSymbol) type;
            var commas = new string(',', nts.TypeParameters.Length - 1);
            return genericTypeWithTypeParameters.Remove(genOpenBraceIndex + 1, genCloseBraceIndex - genOpenBraceIndex - 1).Insert(genOpenBraceIndex + 1, commas);
        }
    }
}
