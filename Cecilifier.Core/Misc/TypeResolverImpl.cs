using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc
{
    internal class TypeResolverImpl : ITypeResolver
    {
        private readonly CecilifierContext _context;

        public TypeResolverImpl(CecilifierContext context)
        {
            _context = context;
            Bcl = new Bcl(this, _context);
        }

        public Bcl Bcl { get; }

        public string Resolve(ITypeSymbol type)
        {
            return ResolveLocalVariableType(type)
                   ?? ResolvePredefinedAndComposedTypes(type)
                   ?? ResolveGenericType(type)
                   ?? Resolve(type.Name);
        }

        public string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");

        public string ResolvePredefinedType(ITypeSymbol type) => $"assembly.MainModule.TypeSystem.{type.Name}";

        private string ResolvePredefinedAndComposedTypes(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return Resolve(array.ElementType) + ".MakeArrayType()";
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return Resolve(pointerType.PointedAtType) + ".MakePointerType()";
            }

            if (type is IFunctionPointerTypeSymbol functionPointer)
            {
                return CecilDefinitionsFactory.FunctionPointerType(this, functionPointer);
            }

            if (type.SpecialType == SpecialType.None || type.TypeKind == TypeKind.Interface || type.SpecialType == SpecialType.System_Enum || type.SpecialType == SpecialType.System_ValueType)
            {
                return null;
            }

            return ResolvePredefinedType(type);
        }

        private string ResolveGenericType(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol { IsGenericType: true } genericTypeSymbol))
            {
                return null;
            }

            if (type.IsTupleType)
            {
                return null;
            }
            
            var genericType = Resolve(OpenGenericTypeName(genericTypeSymbol.ConstructedFrom));
            return MakeGenericInstanceType(genericType, genericTypeSymbol);
        }

        public string ResolveLocalVariableType(ITypeSymbol type)
        {
            var found = _context.DefinitionVariables.GetVariable(type.Name, MemberKind.Type).VariableName ?? _context.DefinitionVariables.GetVariable(type.Name, MemberKind.TypeParameter).VariableName;
            if (found != null && type is INamedTypeSymbol {IsGenericType: true} genericTypeSymbol)
            {
                return MakeGenericInstanceType(found, genericTypeSymbol);
            }
            
            return found;
        }

        private IList<string> CollectTypeArguments(INamedTypeSymbol typeArgumentProvider, List<string> collectTo)
        {
            if (typeArgumentProvider.ContainingType != null)
            {
                CollectTypeArguments(typeArgumentProvider.ContainingType, collectTo);
            }
            collectTo.AddRange(typeArgumentProvider.TypeArguments.Select(Resolve));

            return collectTo;
        }
        
        private string MakeGenericInstanceType(string found, INamedTypeSymbol genericTypeSymbol)
        {
            var typeArgs = CollectTypeArguments(genericTypeSymbol, new List<string>());
            return $"{found}.MakeGenericInstanceType({string.Join(",", typeArgs)})";
        }

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
