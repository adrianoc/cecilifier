using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    public enum MemberKind
    {
        Type,
        TypeParameter,
        Field,
        Method,
        Parameter,
        LocalVariable
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

    public unsafe struct ScopedDefinitionVariable : IDisposable
    {
        private readonly List<DefinitionVariable> _definitionVariables;
        private readonly int _currentSize;
        private delegate*<IList<DefinitionVariable>, int, void> _unregister;

        public ScopedDefinitionVariable(List<DefinitionVariable> definitionVariables, int currentSize, bool dontUnregisterTypesAndMembers = false)
        {
            _definitionVariables = definitionVariables;
            _currentSize = currentSize;
            _unregister = dontUnregisterTypesAndMembers ? &ConditionalUnregister : &UnconditionalUnregister; 
        }

        public void Dispose()
        {
            for (int i = _definitionVariables.Count - 1; i >=  _currentSize; i--)
            {
                _unregister(_definitionVariables, i);
            }
        }
        
        static void ConditionalUnregister(IList<DefinitionVariable> variables, int index)
        {
            var v = variables[index];
            if (v.Kind == MemberKind.LocalVariable || v.Kind == MemberKind.Parameter || v.Kind == MemberKind.TypeParameter) 
                variables.RemoveAt(index);
        }
            
        static void UnconditionalUnregister(IList<DefinitionVariable> variables, int index) => variables.RemoveAt(index);
    }

    public class DefinitionVariableManager
    {
        private readonly List<DefinitionVariable> _definitionStack = new List<DefinitionVariable>();

        private readonly List<DefinitionVariable> _definitionVariables = new List<DefinitionVariable>();

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

        public DefinitionVariable GetLastOf(MemberKind kind)
        {
            var index = _definitionStack.FindLastIndex(c => c.Kind == kind);
            return index switch
            {
                -1 => DefinitionVariable.NotFound,
                _ => _definitionStack[index]
            };
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
            return new ScopedDefinitionVariable(_definitionVariables, _definitionVariables.Count, true);
        }
    }

    internal interface IVisitorContext
    {
        INameStrategy Naming { get; }
        
        string Namespace { get; set; }

        SemanticModel SemanticModel { get; }

        DefinitionVariableManager DefinitionVariables { get; }

        LinkedListNode<string> CurrentLine { get; }
        
        IMethodSymbol GetDeclaredSymbol(BaseMethodDeclarationSyntax methodDeclaration);
        ITypeSymbol GetDeclaredSymbol(BaseTypeDeclarationSyntax classDeclaration);
        TypeInfo GetTypeInfo(TypeSyntax node);
        TypeInfo GetTypeInfo(ExpressionSyntax expressionSyntax);
        INamedTypeSymbol GetSpecialType(SpecialType specialType);

        void WriteCecilExpression(string msg);
        void WriteComment(string comment);
        void WriteNewLine();

        void MoveLineAfter(LinkedListNode<string> instruction, LinkedListNode<string> after);
        
        ITypeResolver TypeResolver { get; }

        #region Flags Handling
        IDisposable WithFlag(string name);
        bool HasFlag(string name);

        #endregion
    }
}
