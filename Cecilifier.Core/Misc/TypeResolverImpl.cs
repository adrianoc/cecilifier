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
            return ResolveTypeLocalVariable(type.Name)
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
            
            if (type.SpecialType == SpecialType.None || type.TypeKind == TypeKind.Interface || type.SpecialType == SpecialType.System_Enum || type.SpecialType == SpecialType.System_ValueType)
            {
                return null;
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
            var args = string.Join(",", genericTypeSymbol.TypeArguments.Select(a => Resolve((ITypeSymbol) a)));
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
