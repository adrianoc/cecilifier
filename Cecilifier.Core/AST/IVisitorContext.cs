using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    public enum MemberKind
    {
        Type,
        Field,
        Method,
        Parameter,
        LocalVariable,
        TryCatchLeaveTarget
    }

    public class DefinitionVariable : IEquatable<DefinitionVariable>
    {
        public static readonly DefinitionVariable NotFound = new DefinitionVariable(string.Empty, string.Empty, default) {IsValid = false};

        public DefinitionVariable(string parentTypeName, string memberName, MemberKind kind, string variableName = null)
        {
            ParentName = parentTypeName;
            MemberName = memberName;
            Kind = kind;
            VariableName = variableName;
            IsValid = true;
        }

        public string MemberName { get; }
        public MemberKind Kind { get; }
        public string VariableName { get; }
        public bool IsValid { get; private set; }
        private string ParentName { get; }

        public bool Equals(DefinitionVariable other)
        {
            return string.Equals(MemberName, other.MemberName)
                   && string.Equals(ParentName, other.ParentName)
                   && Kind == other.Kind;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = MemberName != null ? MemberName.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (ParentName != null ? ParentName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int) Kind;
                return hashCode;
            }
        }

        public override string ToString()
        {
            if (ParentName == null)
            {
                return $"(Kind = {Kind}) {MemberName}";
            }

            return $"(Kind = {Kind}) {ParentName}.{MemberName}";
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is DefinitionVariable other && Equals(other);
        }

        public static implicit operator string(DefinitionVariable variable)
        {
            return variable.VariableName;
        }
    }

    public class MethodDefinitionVariable : DefinitionVariable, IEquatable<MethodDefinitionVariable>
    {
        public MethodDefinitionVariable(string parentTypeName, string methodName, string[] parameterTypeName, string variableName = null) : base(parentTypeName, methodName, MemberKind.Method, variableName)
        {
            Parameters = parameterTypeName;
        }

        private string[] Parameters { get; }

        public bool Equals(MethodDefinitionVariable other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (!base.Equals(other))
            {
                return false;
            }

            if (Parameters.Length != other.Parameters.Length)
            {
                return false;
            }

            for (var i = 0; i < Parameters.Length; i++)
            {
                if (Parameters[i] != other.Parameters[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static bool operator ==(MethodDefinitionVariable lhs, MethodDefinitionVariable rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(MethodDefinitionVariable lhs, MethodDefinitionVariable rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((MethodDefinitionVariable) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (base.GetHashCode() * 397) ^ (Parameters != null ? Parameters.GetHashCode() : 0);
            }
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
        private readonly List<DefinitionVariable> _definitionStack = new List<DefinitionVariable>();

        private readonly List<DefinitionVariable> _definitionVariables = new List<DefinitionVariable>();

        public string LastInstructionVar { get; set; }

        public MethodDefinitionVariable RegisterMethod(string parentName, string methodName, string[] parameterTypes, string definitionVariableName)
        {
            var definitionVariable = new MethodDefinitionVariable(parentName, methodName, parameterTypes, definitionVariableName);
            _definitionVariables.Add(definitionVariable);

            return definitionVariable;
        }

        public DefinitionVariable RegisterNonMethod(string parentName, string memberName, MemberKind memberKind, string definitionVariableName)
        {
            var definitionVariable = new DefinitionVariable(parentName, memberName, memberKind, definitionVariableName);
            _definitionVariables.Add(definitionVariable);

            return definitionVariable;
        }

        public DefinitionVariable GetMethodVariable(MethodDefinitionVariable tbf)
        {
            var methodVars = _definitionVariables.OfType<MethodDefinitionVariable>().Reverse();
            foreach (var candidate in methodVars)
            {
                if (candidate.Equals(tbf))
                {
                    return candidate;
                }
            }

            return DefinitionVariable.NotFound;
        }

        public DefinitionVariable GetVariable(string memberName, MemberKind memberKind, string parentName = null)
        {
            var tbf = new DefinitionVariable(parentName ?? string.Empty, memberName, memberKind);
            for (var i = _definitionVariables.Count - 1; i >= 0; i--)
            {
                if (_definitionVariables[i].Equals(tbf))
                {
                    return _definitionVariables[i];
                }
            }

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
            {
                return DefinitionVariable.NotFound;
            }

            return _definitionStack[index];
        }

        public ScopedDefinitionVariable WithCurrentMethod(string parentName, string memberName, string[] paramTypes, string definitionVariableName)
        {
            var registered = RegisterMethod(parentName, memberName, paramTypes, definitionVariableName);
            _definitionStack.Add(registered);
            return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count - 1);
        }

        public ScopedDefinitionVariable WithCurrent(string parentName, string memberName, MemberKind memberKind, string definitionVariableName)
        {
            var registered = RegisterNonMethod(parentName, memberName, memberKind, definitionVariableName);
            _definitionStack.Add(registered);
            return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count - 1);
        }

        public ScopedDefinitionVariable EnterScope()
        {
            return new ScopedDefinitionVariable(_definitionStack, _definitionStack.Count);
        }
    }

    internal interface IVisitorContext
    {
        string Namespace { get; set; }

        SemanticModel SemanticModel { get; }

        DefinitionVariableManager DefinitionVariables { get; }

        LinkedListNode<string> CurrentLine { get; }
        string this[string name] { get; set; }

        IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
        ITypeSymbol GetDeclaredSymbol(BaseTypeDeclarationSyntax classDeclaration);
        TypeInfo GetTypeInfo(TypeSyntax node);
        TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);
        INamedTypeSymbol GetSpecialType(SpecialType specialType);

        void WriteCecilExpression(string msg);

        int NextFieldId();
        int NextLocalVariableTypeId();
        bool Contains(string name);
        void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);

        event Action<string> InstructionAdded;
        void TriggerInstructionAdded(string instVar);
        
        ITypeResolver TypeResolver { get; }
    }

    internal interface ITypeResolver
    {
        string Resolve(ITypeSymbol type);
        string Resolve(string typeName);
        
        string ResolvePredefinedType(string typeName);
        string ResolvePredefinedType(ITypeSymbol type);
        string ResolvePredefinedAndComposedTypes(ITypeSymbol type);
        string ResolveGenericType(ITypeSymbol type);
        string ResolveTypeLocalVariable(string typeName);
    }
}
