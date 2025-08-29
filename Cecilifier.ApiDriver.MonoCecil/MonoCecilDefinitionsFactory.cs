using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.ApiDriver.MonoCecil;

internal class MonoCecilDefinitionsFactory : DefinitionsFactoryBase, IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers) => RoslynToApiDriverModifiers(type, modifiers);

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
        return CecilDefinitionsFactory.Type(context, typeVar, typeNamespace, typeName, attrs, resolvedBaseType, outerTypeVariable, isStructWithNoFields, interfaces, ownTypeParameters, outerTypeParameters, properties);
    }

    public IEnumerable<string> Method(IVisitorContext context, string methodVar, string methodName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
    {
        return CecilDefinitionsFactory.Method(context, methodVar, methodName, methodModifiers, returnType, refReturn, typeParameters);
    }

    public IEnumerable<string> Method(IVisitorContext context, string declaringTypeName, string methodVar, string methodNameForParameterVariableRegistration, string methodName, string methodModifiers, IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters, Func<IVisitorContext, string> returnTypeResolver, out MethodDefinitionVariable methodDefinitionVariable)
    {
        return CecilDefinitionsFactory.Method(
                            context, 
                            declaringTypeName, 
                            methodVar, 
                            methodNameForParameterVariableRegistration, 
                            methodName, 
                            methodModifiers, 
                            parameters, 
                            typeParameters, 
                            returnTypeResolver,
                            out methodDefinitionVariable);
    }
    
    public IEnumerable<string> Constructor(IVisitorContext context, MemberDefinitionContext memberDefinitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null)
    {
        var ctorName = Utils.ConstructorMethodName(isStatic);
        context.DefinitionVariables.RegisterMethod(typeName, ctorName, paramTypes, 0, memberDefinitionContext.MemberDefinitionVariableName);

        var exp = $@"var {memberDefinitionContext.MemberDefinitionVariableName} = new MethodDefinition(""{ctorName}"", {methodAccessibility} | MethodAttributes.HideBySig | {Constants.Cecil.CtorAttributes}, assembly.MainModule.TypeSystem.Void)";
        if (methodDefinitionPropertyValues != null)
        {
            exp = exp + $"{{ {methodDefinitionPropertyValues} }}";
        }

        return [exp + ";", $"{memberDefinitionContext.ParentDefinitionVariableName}.Methods.Add({memberDefinitionContext.MemberDefinitionVariableName});"];
    }
}
