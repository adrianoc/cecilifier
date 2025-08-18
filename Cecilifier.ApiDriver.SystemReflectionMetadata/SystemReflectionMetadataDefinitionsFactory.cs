using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

internal class SystemReflectionMetadataDefinitionsFactory : IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers)
    {
        //TODO: Return actual attributes ;) 
        return "TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.AutoLayout | TypeAttributes.BeforeFieldInit";
    }

    public IEnumerable<string> Type(
        IVisitorContext context, 
        string typeVar, 
        string typeNamespace, 
        string typeName, 
        string attrs, 
        string resolvedBaseType, 
        DefinitionVariable outerTypeVariable, 
        bool isStructWithNoFields,
        IEnumerable<ITypeSymbol> interfaces, 
        IEnumerable<TypeParameterSyntax>? ownTypeParameters, 
        IEnumerable<TypeParameterSyntax> outerTypeParameters, 
        params string[] properties)
    {
        //TODO: We need to pass the handle of the 1st field/method defined in the module so we need to postpone the type generation after we have visited
        //      all types/members.
        yield return $"""
                      metadata.AddTypeDefinition(
                            {attrs}, // TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.AutoLayout | TypeAttributes.BeforeFieldInit
                            metadata.GetOrAddString("{typeNamespace}"),
                            metadata.GetOrAddString("{typeName}"),
                            {resolvedBaseType},
                            fieldList: MetadataTokens.FieldDefinitionHandle(1),
                            methodList: MetadataTokens.MethodDefinitionHandle(1));
                      """;
    }
}
