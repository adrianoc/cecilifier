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
        var delegateSymbol = ModelExtensions.GetDeclaredSymbol(Context.SemanticModel, node).EnsureNotNull<ISymbol, INamedTypeSymbol>();
        var accessibility = TypeModifiersToCecil(delegateSymbol, node.Modifiers);

        EnsureContainingTypeForwarded(node, delegateSymbol);
        var outerTypeVariable = Context.DefinitionVariables.GetVariable(delegateSymbol.ContainingType?.ToDisplayString(), VariableMemberKind.Type, delegateSymbol.ContainingType?.ContainingSymbol.ToDisplayString());
        var definitionContext = new MemberDefinitionContext(node.Identifier.Text, typeVar, outerTypeVariable.IsValid ? outerTypeVariable.VariableName : null);
        var typeDef = Context.ApiDefinitionsFactory.Type(
                                                    Context,
                                                    definitionContext,
                                                    delegateSymbol.ContainingNamespace?.FullyQualifiedName() ?? string.Empty,
                                                    CecilDefinitionsFactory.DefaultTypeAttributeFor(TypeKind.Delegate, false).AppendEnumFlag(accessibility), 
                                                    Context.RoslynTypeSystem.SystemMulticastDelegate, 
                                                    false, 
                                                    [], 
                                                    node.TypeParameterList?.Parameters, 
                                                    []);

        Context.Generate(typeDef);
        HandleAttributesInMemberDeclaration(node.Identifier.Text, node.AttributeLists, typeVar, VariableMemberKind.Type);

        using (Context.DefinitionVariables.WithCurrent(delegateSymbol.ContainingSymbol?.OriginalDefinition.ToDisplayString() ?? string.Empty, delegateSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, typeVar))
        {
            var ctorLocalVar = Context.Naming.Delegate(node);

            // Delegate ctor
            var parameters = new ParameterSpec[]
            {
                new("target", Context.TypeResolver.Resolve(Context.RoslynTypeSystem.SystemObject, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = "System.Object" },
                new("method", Context.TypeResolver.Resolve(Context.RoslynTypeSystem.SystemIntPtr, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = "System.IntPtr" },
            };
            
            var exps = Context.ApiDefinitionsFactory.Constructor(
                Context, 
                new BodiedMemberDefinitionContext("ctor", ctorLocalVar, typeVar, MemberOptions.IsRuntime, null), 
                node.Identifier.Text, 
                false, 
                "MethodAttributes.FamANDAssem | MethodAttributes.Family", 
                parameters);
            Context.Generate(exps);

            var invokeMethodVar = Context.Naming.SyntheticVariable("Invoke", ElementKind.Method);
            var invokeParameters = node.ParameterList.Parameters.Select(p => p.ToParameterSpec(Context, invokeMethodVar)).ToArray();
            // Invoke() method
            AddDelegateMethod(
                node.Identifier.Text,
                typeVar,
                "Invoke",
                invokeMethodVar,
                ResolveType(node.ReturnType, ResolveTargetKind.ReturnType),
                invokeParameters);
            
            // BeginInvoke() method
            var beginInvokeMethodVar = Context.Naming.SyntheticVariable("BeginInvoke", ElementKind.Method);
            IReadOnlyList<ParameterSpec> beginInvokeParameters =
            [
                ..node.ParameterList.Parameters.Select(p => p.ToParameterSpec(Context, beginInvokeMethodVar)),
                new("asyncCallback", Context.TypeResolver.Resolve(Context.RoslynTypeSystem.SystemAsyncCallback, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None),
                new("target", Context.TypeResolver.Resolve(Context.RoslynTypeSystem.SystemObject, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None)
            ];

            var asyncResultTypeSymbol = Context.RoslynTypeSystem.ForType<IAsyncResult>();
            AddDelegateMethod(
                    node.Identifier.Text,
                    typeVar,
                    "BeginInvoke",
                    beginInvokeMethodVar,
                    Context.TypeResolver.Resolve(asyncResultTypeSymbol, ResolveTargetKind.ReturnType),
                    beginInvokeParameters);

            // EndInvoke() method
            var endInvokeMethodVar = Context.Naming.SyntheticVariable("EndInvoke", ElementKind.Method);
            var endInvokeExps = Context.ApiDefinitionsFactory.Method(
                                                                        Context,
                                                                        new BodiedMemberDefinitionContext("EndInvoke", endInvokeMethodVar, typeVar, MemberOptions.IsRuntime, null),
                                                                        "declaringTypeName",
                                                                        Constants.Cecil.DelegateMethodAttributes,
                                                                        [new ParameterSpec("ar", Context.TypeResolver.Resolve(asyncResultTypeSymbol, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None)],
                                                                        [],
                                                                        ctx => ctx.TypeResolver.Resolve(Context.GetTypeInfo(node.ReturnType).Type, ResolveTargetKind.ReturnType),
                                                                        out var _);
            Context.Generate(endInvokeExps);
            
            base.VisitDelegateDeclaration(node);
            Context.OnFinishedTypeDeclaration(delegateSymbol);
        }

        return;

        void AddDelegateMethod(string delegateName, string delegateTypeVariable, string methodName, string methodVar, ResolvedType returnType, IReadOnlyList<ParameterSpec> parameters)
        {
            BodiedMemberDefinitionContext methodDefinitionContext = new(methodName, methodVar, delegateTypeVariable, MemberOptions.IsRuntime, null); 
            var methodToAdd = Context.ApiDefinitionsFactory.Method(
                                                    Context,
                                                    methodDefinitionContext,
                                                    delegateName,
                                                    Constants.Cecil.DelegateMethodAttributes,
                                                    parameters,
                                                    [],
                                                    ctx => returnType,
                                                    out var _);
            Context.Generate(methodToAdd);
        }
    }

    private void EnsureContainingTypeForwarded(DelegateDeclarationSyntax delegateDeclaration, INamedTypeSymbol delegateSymbol)
    {
        if (delegateSymbol.ContainingType == null)
            return;
        EnsureForwardedTypeDefinition(Context, delegateSymbol.ContainingType, delegateDeclaration.TypeParameterList?.Parameters);
    }
}
