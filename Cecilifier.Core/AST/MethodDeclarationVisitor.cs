using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    class MethodDeclarationVisitor : SyntaxWalkerBase
    {
        protected string ilVar;

        public MethodDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var methodSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull<ISymbol, IMethodSymbol>($"Method symbol for {node.Identifier} could not be resolved.");
            if (!NoCapturedVariableValidator.IsValid(Context, node))
            {
                Context.WriteComment("To make ensure local functions dont capture variables, declare them as static.");
            }
            
            // Local functions have a well defined list of modifiers.
            var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword));
            if (methodSymbol.IsStatic)
                modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));

            // local functions are not first class citizens wrt variable naming... handle them as methods for now.
            var localFunctionVar = Context.Naming.SyntheticVariable(node.Identifier.Text, ElementKind.Method);
            ProcessMethodDeclarationInternal(
                node,
                methodSymbol.ContainingType.Name,
                localFunctionVar,
                methodSymbol,
                modifiers,
                node.Identifier.Text,
                $"<{methodSymbol.ContainingSymbol.Name}>g__{node.Identifier.Text}|0_0",
                false,
                s => { base.VisitLocalFunctionStatement(node); },
                node.AttributeLists,
                node.ParameterList.Parameters,
                node.TypeParameterList?.Parameters.ToArray());

        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            ExpressionVisitor.Visit(Context, ilVar, node);
        }

        public override void VisitBlock(BlockSyntax node)
        {
            StatementVisitor.Visit(Context, ilVar, node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var refReturn = node.ReturnType is RefTypeSyntax;

            ProcessMethodDeclaration(
                node,
                Context.Naming.MethodDeclaration(node),
                node.Identifier.ValueText,
                MethodNameOf(node),
                refReturn,
                _ => base.VisitMethodDeclaration(node),
                node.TypeParameterList?.Parameters.ToArray());
        }

        public override void VisitParameterList(ParameterListSyntax node)
        {
            if (node.Parameters.Count > 0)
            {
                Context.WriteNewLine();
                Context.WriteComment($"Parameters of '{node.Parent.HumanReadableSummary()}'");
            }
            base.VisitParameterList(node);
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            var paramVar = Context.Naming.Parameter(node);

            var methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
            if (!methodVar.IsValid)
                throw new InvalidOperationException($"Failed to retrieve current method.");

            using var _ = LineInformationTracker.Track(Context, node);
            
            var containingSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull().ContainingSymbol;
            var forwardedParamVar = Context.DefinitionVariables.GetVariable(node.Identifier.ValueText, VariableMemberKind.Parameter, containingSymbol.ToDisplayString());
            if (forwardedParamVar.IsValid)
            {
                paramVar = forwardedParamVar.VariableName;
            }
            else
            {
                Context.DefinitionVariables.RegisterNonMethod(containingSymbol.ToDisplayString(), node.Identifier.ValueText, VariableMemberKind.Parameter, paramVar);
                var exps = CecilDefinitionsFactory.Parameter(Context, node, methodVar.VariableName, paramVar);
                AddCecilExpressions(Context, exps);
            }

            HandleAttributesInMemberDeclaration(node.AttributeLists, paramVar);

            base.VisitParameter(node);
        }

        private void ProcessMethodDeclarationInternal(
            SyntaxNode node,
            string declaringTypeName,
            string variableName,
            IMethodSymbol methodSymbol,
            SyntaxTokenList modifiersTokens,
            string simpleName,
            string methodName,
            bool refReturn,
            Action<string> runWithCurrent,
            SyntaxList<AttributeListSyntax> attributes,
            SeparatedSyntaxList<ParameterSyntax> parameters,
            IList<TypeParameterSyntax> typeParameters = null)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            using (Context.DefinitionVariables.EnterLocalScope())
            {
                typeParameters ??= Array.Empty<TypeParameterSyntax>();
                var methodVar = AddOrUpdateMethodDefinition(
                                            declaringTypeName,
                                            variableName,
                                            // for ctors we want to use the `methodName` (== .ctor) instead of the `simpleName` (== ctor) otherwise we may fail to find existing variables.
                                            methodSymbol.MethodKind == MethodKind.Constructor ? methodName : simpleName,
                                            methodName,
                                            modifiersTokens.MethodModifiersToCecil(GetSpecificModifiers(), methodSymbol),
                                            methodSymbol.ReturnType,
                                            refReturn,
                                            parameters,
                                            typeParameters);

                AddCecilExpression("{0}.Methods.Add({1});", Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName, methodVar);

                HandleAttributesInMemberDeclaration(attributes, TargetDoesNotMatch, SyntaxKind.ReturnKeyword, methodVar); // Normal method attrs.
                HandleAttributesInMemberDeclaration(attributes, TargetMatches, SyntaxKind.ReturnKeyword, $"{methodVar}.MethodReturnType"); // [return:Attr]

                AddToOverridenMethodsIfAppropriated(methodVar, methodSymbol);

                if (modifiersTokens.IndexOf(SyntaxKind.ExternKeyword) == -1)
                {
                    if (methodSymbol.HasCovariantReturnType())
                    {
                        AddCecilExpression($"{methodVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(System.Runtime.CompilerServices.PreserveBaseOverridesAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
                    }

                    if (!methodSymbol.IsAbstract)
                    {
                        ilVar = Context.Naming.ILProcessor(simpleName);
                        AddCecilExpression($"{methodVar}.Body.InitLocals = true;");
                        AddCecilExpression($"var {ilVar} = {methodVar}.Body.GetILProcessor();");
                    }

                    // if the method is a local function use `simpleName` as the method name (instead of `methodName`) since, in this context,
                    // the later is a `mangled name` and any reference to the method will use its `unmangled name` for lookups which would fail
                    // should we use `methodName` as the registered name.
                    var nameUsedInRegisteredVariable = methodSymbol.MethodKind == MethodKind.LocalFunction ? simpleName : methodName;
                    WithCurrentMethod(declaringTypeName, methodVar, nameUsedInRegisteredVariable, parameters.Select(p => Context.SemanticModel.GetDeclaredSymbol(p).Type.ToDisplayString()).ToArray(), methodSymbol.TypeParameters.Length, runWithCurrent);
                    if (!methodSymbol.IsAbstract && !node.DescendantNodes().Any(n => n.IsKind(SyntaxKind.ReturnStatement)))
                    {
                        Context.EmitCilInstruction(ilVar, OpCodes.Ret);
                    }
                }
                else
                {
                    Context.DefinitionVariables.RegisterMethod(declaringTypeName, methodName, parameters.Select(p => Context.GetTypeInfo(p.Type).Type.ToDisplayString()).ToArray(), typeParameters.Count, methodVar);
                }
            }
        }

        private string AddOrUpdateMethodDefinition(string declaringTypeName, string variableName, string simpleName, string methodName, string methodModifiers, ITypeSymbol returnType, bool refReturn, SeparatedSyntaxList<ParameterSyntax> parameters, IList<TypeParameterSyntax> typeParameters)
        {
            var tbf = new MethodDefinitionVariable(declaringTypeName, simpleName, parameters.Select(paramSyntax => Context.GetTypeInfo(paramSyntax.Type).Type.ToDisplayString()).ToArray(), typeParameters.Count);
            var found = Context.DefinitionVariables.GetMethodVariable(tbf);
            if (found.IsValid)
            {
                AddCecilExpression("{0}.Attributes = {1};", found.VariableName, methodModifiers);
                AddCecilExpression("{0}.HasThis = !{0}.IsStatic;", found.VariableName);
                return found.VariableName;
            }

            AddMethodDefinition(Context, variableName, methodName, methodModifiers, returnType, refReturn, typeParameters);
            return variableName;
        }

        protected void ProcessMethodDeclaration<T>(T node, string variableName, string simpleName, string fqName, bool refReturn, Action<string> runWithCurrent, IList<TypeParameterSyntax> typeParameters = null) where T : BaseMethodDeclarationSyntax
        {
            var methodSymbol = Context.GetDeclaredSymbol(node);
            ProcessMethodDeclarationInternal(
                            node,
                            methodSymbol.ContainingSymbol.Name,
                            variableName,
                            methodSymbol,
                            node.Modifiers,
                            simpleName,
                            fqName,
                            refReturn,
                            runWithCurrent,
                            node.AttributeLists,
                            node.ParameterList.Parameters,
                            typeParameters);
        }

        public static void AddMethodDefinition(IVisitorContext context, string methodVar, string methodName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
        {
            context.WriteNewLine();
            context.WriteComment($"Method : {methodName}");

            TypeDeclarationVisitor.EnsureForwardedTypeDefinition(context, returnType, Array.Empty<TypeParameterSyntax>());
            var exps = CecilDefinitionsFactory.Method(context, methodVar, methodName, methodModifiers, returnType, refReturn, typeParameters);
            AddCecilExpressions(context, exps);
            
            HandleAttributesInTypeParameter(context, typeParameters);
        }

        protected virtual string GetSpecificModifiers() => null;

        private string MethodNameOf(MethodDeclarationSyntax method)
        {
            return DeclaredSymbolFor(method).Name;
        }
    }
}
