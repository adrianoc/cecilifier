#nullable enable
using System;
using System.Collections.Generic;
using Cecilifier.Core.AST;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.ApiDriver;

public interface IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers);

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
        params string[] properties);

    public IEnumerable<string> Method(IVisitorContext context, string methodVar, string methodName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters);

    public IEnumerable<string> Method(
        IVisitorContext context,
        string declaringTypeName,
        string methodVar,
        string methodNameForParameterVariableRegistration, // we can't use the method name in some scenarios (indexers, for instance) 
        string methodName,
        string methodModifiers,
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        Func<IVisitorContext, string> returnTypeResolver,
        out MethodDefinitionVariable methodDefinitionVariable);

    public IEnumerable<string> Constructor(IVisitorContext context, MemberDefinitionContext memberDefinitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null);
}

public record struct MemberDefinitionContext(
                            string MemberDefinitionVariableName, 
                            string ParentDefinitionVariableName, 
                            IlContext IlContext);
