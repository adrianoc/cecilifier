using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
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
        var typeDef = Context.ApiDefinitionsFactory.Type(
                                                    Context, 
                                                    typeVar, 
                                                    delegateSymbol.ContainingNamespace?.FullyQualifiedName() ?? string.Empty, 
                                                    node.Identifier.ValueText, 
                                                    CecilDefinitionsFactory.DefaultTypeAttributeFor(TypeKind.Delegate, false).AppendModifier(accessibility), 
                                                    Context.TypeResolver.Bcl.System.MulticastDelegate, 
                                                    outerTypeVariable, 
                                                    false, 
                                                    [], 
                                                    node.TypeParameterList?.Parameters, 
                                                    [], 
                                                    "IsAnsiClass = true");

        AddCecilExpressions(Context, typeDef);
        HandleAttributesInMemberDeclaration(node.AttributeLists, typeVar);

        using (Context.DefinitionVariables.WithCurrent(delegateSymbol.ContainingSymbol?.OriginalDefinition.ToDisplayString() ?? string.Empty, delegateSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, typeVar))
        {
            var ctorLocalVar = Context.Naming.Delegate(node);

            // Delegate ctor
            string[] paramTypes = ["System.Object", "System.IntPtr"];
            var exps = Context.ApiDefinitionsFactory.Constructor(
                Context, 
                new BodiedMemberDefinitionContext("ctor", ctorLocalVar, typeVar, MemberOptions.None, IlContext.None), 
                node.Identifier.Text, 
                false, 
                "MethodAttributes.FamANDAssem | MethodAttributes.Family", 
                paramTypes, 
                "IsRuntime = true");
            Context.Generate(exps);
            AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.Object}));");
            AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.IntPtr}));");

            // Invoke() method
            AddDelegateMethod(
                typeVar,
                "Invoke",
                ResolveType(node.ReturnType, ResolveTargetKind.ReturnType),
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
            var endInvokeExps = Context.ApiDefinitionsFactory.Method(
                                                                        Context,
                                                                        new BodiedMemberDefinitionContext("EndInvoke", endInvokeMethodVar, typeVar, MemberOptions.None, IlContext.None),
                                                                        "declaringTypeName",
                                                                        Constants.Cecil.DelegateMethodAttributes,
                                                                        [new ParameterSpec("ar", Context.TypeResolver.Bcl.System.IAsyncResult, RefKind.None, Constants.ParameterAttributes.None)],
                                                                        [],
                                                                        ctx => ctx.TypeResolver.ResolveAny(Context.GetTypeInfo(node.ReturnType).Type, ResolveTargetKind.ReturnType),
                                                                        out var _);

            endInvokeExps = endInvokeExps.Concat([$"{endInvokeMethodVar}.HasThis = true;", $"{endInvokeMethodVar}.IsRuntime = true;"]);
            AddCecilExpressions(Context, endInvokeExps);
            
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
