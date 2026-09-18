#nullable enable
using System;
using System.Collections.Generic;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.ApiDriver.DefinitionsFactory;

public interface IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers);

    /// <summary>Generates the code for a type declaration.</summary>
    /// <remarks>
    /// 1. At IL level, type parameters from *outer* types are considered to be part of a inner type whence these type parameters need to be added to the list of type parameters even
    ///    if the type being declared is not a generic type.
    /// 
    /// 2. Only type parameters owned by the type being declared are considered when computing the arity of the type (whence the number following the backtick reflects only the
    ///    # of the type parameters declared by the type being declared).
    /// </remarks>
    /// <param name="context"></param>
    /// <param name="definitionContext"></param>
    /// <param name="typeNamespace"></param>
    /// <param name="attrs"></param>
    /// <param name="baseType"></param>
    /// <param name="isStructWithNoFields"></param>
    /// <param name="interfaces"></param>
    /// <param name="ownTypeParameters"></param>
    /// <param name="outerTypeParameters"></param>
    /// <param name="properties"></param>
    /// <returns></returns>
    public IEnumerable<string> Type(
        IVisitorContext context,
        MemberDefinitionContext definitionContext,
        string typeNamespace,
        string attrs,
        ITypeSymbol? baseType,
        bool isStructWithNoFields,
        IEnumerable<ITypeSymbol> interfaces,
        IEnumerable<TypeParameterSyntax>? ownTypeParameters,
        IEnumerable<TypeParameterSyntax> outerTypeParameters,
        params TypeLayoutProperty[] properties);

    void UpdateBaseTypeIfNeeded(IVisitorContext context, ITypeSymbol typeSymbol, string typeDefinitionVariable);
    
    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, BodiedMemberDefinitionContext bodiedMemberDefinitionContext, string methodName, string methodModifiers, IList<TypeParameterSyntax> typeParameters);

    /// <summary>Generates the statements using the Api Driver API to emit the specified method.</summary>
    /// <param name="context">The visitor context used during processing.</param>
    /// <param name="definitionContext">Details about the method being defined.</param>
    /// <param name="declaringTypeName">The name of the declaring type.</param>
    /// <param name="methodModifiers">Modifiers to be applied to the method.</param>
    /// <param name="parameters">List of parameters.</param>
    /// <param name="typeParameters">In case of generic methods lists all method Type Parameters; empty otherwise.</param>
    /// <param name="returnTypeResolver">A Fun&lt;T&gt; responsible to resolve the method return's type.</param>
    /// <param name="methodDefinitionVariable">the <see cref="MethodDefinitionVariable"/> used to store the data representing the just emitted method. This is used when emitting code that references the method.</param>
    /// <remarks>
    /// This overload is best suited to emit methods being synthetyzed by an Api Driver, i.e. in scenarios in which the method is not present in the source code; this is most
    /// commonly seem when dealing with code that C# compiler 'lowers' before emitting the IL (for instance, for inline arrays C# compiler emits an extra type named
    /// &lt;PrivateImplementationDetail&gt; with multiple methods.
    /// </remarks>
    /// <returns>A list of statements to emit the method using the Api Driver API.</returns>
    public IEnumerable<string> Method(IVisitorContext context,
        BodiedMemberDefinitionContext definitionContext,
        string declaringTypeName,
        string methodModifiers, //TODO: Try to change to MethodAttributes enum (reflection) in the same way we do with IL opcodes (and remove MemberDefinitionContext.Options)
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        Func<IVisitorContext, ResolvedType> returnTypeResolver,
        out MethodDefinitionVariable methodDefinitionVariable // we can't use the method name in some scenarios (indexers, for instance) 
    );

    public IEnumerable<string> Constructor(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null);
    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, in FieldInitializationData initializer = default);
    public IEnumerable<string> Field(IVisitorContext context, MemberDefinitionContext definitionContext, string declaringTypeName, ResolvedType fieldType, string fieldAttributes, bool isVolatile, bool isByRef, FieldInitializationData initializer = default);
    public IEnumerable<string> FieldReference(IVisitorContext context, string fieldReferenceVariable, string fieldName, ResolvedType fieldType, in ResolvedType declaringType);
    
    IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, ResolvedType[] localVariableTypes, InstructionRepresentation[] instructions);
    DefinitionVariable LocalVariable(IVisitorContext context, string variableName, string methodDefinitionVariableName, ResolvedType resolvedType);
    IEnumerable<string> Property(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string declaringTypeName, List<ParameterSpec> propertyParameters, ResolvedType propertyType);
    IEnumerable<string> Attribute(IVisitorContext context, IMethodSymbol attributeCtor, string attributeVarBaseName, string attributeTargetVar, VariableMemberKind targetKind, params CustomAttributeArgument[] arguments);

    IEnumerable<string> Event(IVisitorContext context, BodiedMemberDefinitionContext eventSpec, string declaringTypeName, ResolvedType eventType, string addAccessorVariable, string removeAccessorVariable);

    /// <summary>
    /// Emits code to override a method from a base class.
    /// </summary>
    /// <param name="context">Visitor context to use.</param>
    /// <param name="overriderMethodVar">Expression representing the method overriding the base method. Note that commonly this is the name of a variable holding the data representing a method definition.</param>
    /// <param name="overridenMethod">
    /// Expression representing the base method being overriden. If this is 'null' <paramref name="overriderMethodVar"/> does not override any method and whence no code is generated."/>
    /// </param>
    void OverrideBaseMethod(IVisitorContext context, string overriderMethodVar, string? overridenMethod);

    IEnumerable<string> PInvoke(IVisitorContext context, string moduleName, string methodVar, string methodName, ReadOnlySpan<CustomAttributeArgument> customAttributeArguments);
}
