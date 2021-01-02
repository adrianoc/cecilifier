using System;
using System.Linq;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc
{
    internal class TypeResolverImpl : ITypeResolver
    {
        private readonly CecilifierContext _context;

        public TypeResolverImpl(CecilifierContext context)
        {
            _context = context;
        }

        public string Resolve(ITypeSymbol type)
        {
            return ResolveTypeLocalVariable(type)
                   ?? ResolvePredefinedAndComposedTypes(type)
                   ?? ResolveGenericType(type)
                   ?? Resolve(type.Name);
        }

        public string Resolve(string typeName) => Utils.ImportFromMainModule($"typeof({typeName})");
        
        public string ResolvePredefinedType(string typeName) => $"assembly.MainModule.TypeSystem.{typeName}";

        public string ResolvePredefinedType(ITypeSymbol type) => ResolvePredefinedType(type.Name);

        public string ResolvePredefinedAndComposedTypes(ITypeSymbol type)
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

            return ResolvePredefinedType(type.Name);
        }

        public string ResolveGenericType(ITypeSymbol type)
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

        public string ResolveTypeLocalVariable(ITypeSymbol type)
        {
            var found = _context.DefinitionVariables.GetVariable(type.Name, MemberKind.Type).VariableName ?? _context.DefinitionVariables.GetVariable(type.Name, MemberKind.TypeParameter).VariableName;
            if (found != null && type is INamedTypeSymbol {IsGenericType: true} genericTypeSymbol)
            {
                return MakeGenericInstanceType(found, genericTypeSymbol);
            }
            
            return found;
        }

        private string MakeGenericInstanceType(string found, INamedTypeSymbol genericTypeSymbol)
        {
            var args = string.Join(",", genericTypeSymbol.TypeArguments.Select(Resolve));
            return $"{found}.MakeGenericInstanceType({args})";
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
