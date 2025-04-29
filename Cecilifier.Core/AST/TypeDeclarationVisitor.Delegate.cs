using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal partial class TypeDeclarationVisitor
{
    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        Context.WriteNewLine();
        Context.WriteComment($"Delegate: {node.Identifier.Text}");
        
        var typeVar = Context.Naming.Delegate(node);
        var delegateSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull<ISymbol, INamedTypeSymbol>();
        var accessibility = TypeModifiersToCecil(delegateSymbol, node.Modifiers);

        EnsureContainingTypeForwarded(node, delegateSymbol);
        var outerTypeVariable = Context.DefinitionVariables.GetVariable(delegateSymbol.ContainingType?.ToDisplayString(), VariableMemberKind.Type, delegateSymbol.ContainingType?.ContainingSymbol.ToDisplayString());
        var typeDef = CecilDefinitionsFactory.Type(
            Context,
            typeVar,
            delegateSymbol.ContainingNamespace?.FullyQualifiedName() ?? string.Empty,
            node.Identifier.ValueText,
            CecilDefinitionsFactory.DefaultTypeAttributeFor(TypeKind.Delegate, false).AppendModifier(accessibility),
            Context.TypeResolver.Bcl.System.MulticastDelegate,
            outerTypeVariable,
            isStructWithNoFields:false,
            Array.Empty<ITypeSymbol>(),
            node.TypeParameterList?.Parameters,
            Array.Empty<TypeParameterSyntax>(),
            "IsAnsiClass = true");

        AddCecilExpressions(Context, typeDef);
        HandleAttributesInMemberDeclaration(node.AttributeLists, typeVar);

        using (Context.DefinitionVariables.WithCurrent(delegateSymbol.ContainingSymbol?.OriginalDefinition.ToDisplayString() ?? string.Empty, delegateSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, typeVar))
        {
            var ctorLocalVar = Context.Naming.Delegate(node);

            // Delegate ctor
            AddCecilExpression(CecilDefinitionsFactory.Constructor(Context, ctorLocalVar, node.Identifier.Text, false, "MethodAttributes.FamANDAssem | MethodAttributes.Family",
                new[] { "System.Object", "System.IntPtr" }, "IsRuntime = true"));
            AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.Object}));");
            AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.IntPtr}));");
            AddCecilExpression($"{typeVar}.Methods.Add({ctorLocalVar});");

            // Invoke() method
            AddDelegateMethod(
                typeVar,
                "Invoke",
                ResolveType(node.ReturnType),
                node.ParameterList.Parameters,
                (methodVar, param) => CecilDefinitionsFactory.Parameter(Context, param, methodVar, Context.Naming.Parameter(param)));

            // BeginInvoke() method
            var beginInvokeMethodVar = AddDelegateMethod(
                 typeVar,
                 "BeginInvoke",
                 Context.TypeResolver.Bcl.System.IAsyncResult,
                 node.ParameterList.Parameters,
                 (methodVar, param) => CecilDefinitionsFactory.Parameter(Context, param, methodVar, Context.Naming.Parameter(param)));

            AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.AsyncCallback}));");
            AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.Object}));");

            // EndInvoke() method
            var endInvokeMethodVar = Context.Naming.SyntheticVariable("EndInvoke", ElementKind.Method);

            var endInvokeExps = CecilDefinitionsFactory.Method(
                Context,
                endInvokeMethodVar,
                "EndInvoke",
                Constants.Cecil.DelegateMethodAttributes,
                Context.GetTypeInfo(node.ReturnType).Type,
                false,
                Array.Empty<TypeParameterSyntax>()
            );

            endInvokeExps = endInvokeExps.Concat(new[] { $"{endInvokeMethodVar}.HasThis = true;", $"{endInvokeMethodVar}.IsRuntime = true;", });

            var endInvokeParamExps = CecilDefinitionsFactory.Parameter(
                "ar",
                RefKind.None,
                paramsAttributeTypeName: null,
                endInvokeMethodVar,
                Context.Naming.Parameter("ar"),
                Context.TypeResolver.Bcl.System.IAsyncResult,
                Constants.ParameterAttributes.None,
                (null, false));

            AddCecilExpressions(Context, endInvokeExps);
            AddCecilExpressions(Context, endInvokeParamExps);
            AddCecilExpression($"{typeVar}.Methods.Add({endInvokeMethodVar});");

            base.VisitDelegateDeclaration(node);
        }

        string AddDelegateMethod(string typeLocalVar, string methodName, string resolvedReturnType, in SeparatedSyntaxList<ParameterSyntax> parameters, Func<string, ParameterSyntax, IEnumerable<string>> parameterHandler)
        {
            var methodLocalVar = Context.Naming.SyntheticVariable(methodName, ElementKind.Method);
            AddCecilExpression(
                $@"var {methodLocalVar} = new MethodDefinition(""{methodName}"", {Constants.Cecil.DelegateMethodAttributes}, {resolvedReturnType})
				{{
					HasThis = true,
					IsRuntime = true,
				}};");

            foreach (var param in parameters)
            {
                AddCecilExpressions(Context, parameterHandler(methodLocalVar, param));
            }

            AddCecilExpression($"{typeLocalVar}.Methods.Add({methodLocalVar});");
            return methodLocalVar;
        }
    }

    private void EnsureContainingTypeForwarded(DelegateDeclarationSyntax delegateDeclaration, INamedTypeSymbol delegateSymbol)
    {
        if (delegateSymbol.ContainingType == null)
            return;
        EnsureForwardedTypeDefinition(Context, delegateSymbol.ContainingType, delegateDeclaration.TypeParameterList?.Parameters);
    }
}
