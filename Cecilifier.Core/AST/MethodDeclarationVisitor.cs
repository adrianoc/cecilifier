using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;

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
            var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword));

            // local functions are not first class citizens wrt variable naming... handle them as methods for now.
            var localFunctionVar = Context.Naming.SyntheticVariable(node.Identifier.Text, ElementKind.Method);
            ProcessMethodDeclarationInternal(
                node,
                localFunctionVar,
                methodSymbol,
                modifiers,
                node.Identifier.Text,
                $"<{methodSymbol.ContainingSymbol.Name}>g__{node.Identifier.Text}|0_0",
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
                throw new InvalidOperationException("Failed to retrieve current method.");

            using var _ = LineInformationTracker.Track(Context, node);
            
            var containingSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull().ContainingSymbol;
            var forwardedParamVar = Context.DefinitionVariables.GetVariable(node.Identifier.ValueText, VariableMemberKind.Parameter, containingSymbol.OriginalDefinition.ToDisplayString());
            if (forwardedParamVar.IsValid)
            {
                paramVar = forwardedParamVar.VariableName;
            }
            else
            {
                Context.DefinitionVariables.RegisterNonMethod(containingSymbol.OriginalDefinition.ToDisplayString(), node.Identifier.ValueText, VariableMemberKind.Parameter, paramVar);
                var exps = CecilDefinitionsFactory.Parameter(Context, node, methodVar.VariableName, paramVar);
                AddCecilExpressions(Context, exps);
            }

            HandleAttributesInMemberDeclaration(node.AttributeLists, paramVar);

            base.VisitParameter(node);
        }

        private void ProcessMethodDeclarationInternal(
            SyntaxNode node,
            string variableName,
            IMethodSymbol methodSymbol,
            SyntaxTokenList modifiersTokens,
            string simpleName,
            string methodName,
            Action<string> runWithCurrent,
            SyntaxList<AttributeListSyntax> attributes,
            SeparatedSyntaxList<ParameterSyntax> parameters,
            IList<TypeParameterSyntax> typeParameters = null)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            using (Context.DefinitionVariables.EnterLocalScope())
            {
                typeParameters ??= Array.Empty<TypeParameterSyntax>();
                var declaringTypeName = methodSymbol.ContainingSymbol.Kind == SymbolKind.Method 
                                                            ? methodSymbol.ContainingType.Name
                                                            : methodSymbol.ContainingSymbol.ToDisplayString();
                
                var methodVar = AddOrUpdateMethodDefinition(
                                            methodSymbol,
                                            declaringTypeName,
                                            variableName,
                                            simpleName,
                                            methodName,
                                            modifiersTokens.MethodModifiersToCecil(GetSpecificModifiers(), methodSymbol),
                                            parameters,
                                            typeParameters);

                HandleAttributesInMemberDeclaration(attributes, TargetDoesNotMatch, SyntaxKind.ReturnKeyword, methodVar); // Normal method attrs.
                HandleAttributesInMemberDeclaration(attributes, TargetMatches, SyntaxKind.ReturnKeyword, $"{methodVar}.MethodReturnType"); // [return:Attr]

                AddToOverridenMethodsIfAppropriated(methodVar, methodSymbol);

                if (modifiersTokens.IndexOf(SyntaxKind.ExternKeyword) == -1)
                {
                    if (methodSymbol.HasCovariantReturnType())
                    {
                        AddCecilExpression($"{methodVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(System.Runtime.CompilerServices.PreserveBaseOverridesAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
                    }

                    // if the method is a local function use `simpleName` as the method name (instead of `methodName`) since, in this context,
                    // the latter is a `mangled name` and any reference to the method will use its `unmangled name` for lookups which would fail
                    // should we use `methodName` as the registered name.
                    var nameUsedInRegisteredVariable = methodSymbol.MethodKind == MethodKind.LocalFunction ? simpleName : methodName;
                    WithCurrentMethod(declaringTypeName, methodVar, nameUsedInRegisteredVariable, parameters.Select(p => Context.SemanticModel.GetDeclaredSymbol(p).Type.ToDisplayString()).ToArray(), methodSymbol.TypeParameters.Length, runWithCurrent);
                    if (!methodSymbol.IsAbstract && !node.DescendantNodes().Any(n => n.IsKind(SyntaxKind.ReturnStatement)))
                    {
                        Context.ApiDriver.EmitCilInstruction(Context, ilVar, OpCodes.Ret);
                    }
                }
                else
                {
                    Context.DefinitionVariables.RegisterMethod(declaringTypeName, methodName, parameters.Select(p => Context.GetTypeInfo(p.Type).Type.ToDisplayString()).ToArray(), typeParameters.Count, methodVar);
                }
            }
        }

        private string AddOrUpdateMethodDefinition(IMethodSymbol methodSymbol, string declaringTypeName, string variableName, string simpleName, string methodName, string methodModifiers, SeparatedSyntaxList<ParameterSyntax> parameters, IList<TypeParameterSyntax> typeParameters)
        {
            // for ctors we want to use the `methodName` (== .ctor) instead of the `simpleName` (== ctor) otherwise we may fail to find existing variables.
            var tbf = new MethodDefinitionVariable(
                    declaringTypeName, 
                    methodSymbol.MethodKind == MethodKind.Constructor ? methodName : simpleName, 
                    parameters.Select(paramSyntax => Context.GetTypeInfo(paramSyntax.Type).Type.ToDisplayString()).ToArray(), 
                    typeParameters.Count);
            
            var found = Context.DefinitionVariables.GetMethodVariable(tbf);
            if (found.IsValid)
            {
                AddCecilExpression("{0}.Attributes = {1};", found.VariableName, methodModifiers);
                AddCecilExpression("{0}.HasThis = !{0}.IsStatic;", found.VariableName);
                
                //TODO: Temporary hack to set `ilVar` until we change that to `IlContext` and assign `NewIlContext()` call
                //      inside AddMethodDefinition() call bellow.
                if (ilVar == null && !methodSymbol.IsExtern)
                {
                    var ilContext = Context.ApiDriver.NewIlContext(Context, simpleName, found.VariableName);
                    ilVar = ilContext.VariableName;
                }
                //TODO: Move this code to IApiDriverDefinitionsFactory.XXXForwardedMethod() (in SRM most likely we'll do nothing)
                if (Context.GetType().Name == "MonoCecilContext")
                {
                    var declaringTypeVarName = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;
                    Context.Generate($"{declaringTypeVarName}.Methods.Add({found.VariableName});");
                }
                return found.VariableName;
            }

            AddMethodDefinition(Context, methodSymbol, variableName, methodName, simpleName, methodModifiers, parameters, typeParameters);
            return variableName;
        }

        protected void ProcessMethodDeclaration<T>(T node, string variableName, string simpleName, string fqName, bool refReturn, Action<string> runWithCurrent, IList<TypeParameterSyntax> typeParameters = null) where T : BaseMethodDeclarationSyntax
        {
            var methodSymbol = Context.GetDeclaredSymbol(node);
            ProcessMethodDeclarationInternal(
                            node,
                            variableName,
                            methodSymbol,
                            node.Modifiers,
                            simpleName,
                            fqName,
                            runWithCurrent,
                            node.AttributeLists,
                            node.ParameterList.Parameters,
                            typeParameters);
        }

        private void AddMethodDefinition(
                                    IVisitorContext context,
                                    IMethodSymbol methodSymbol,
                                    string methodVar, 
                                    string methodName, 
                                    string simpleName, 
                                    string methodModifiers, 
                                    SeparatedSyntaxList<ParameterSyntax> parameters, 
                                    IList<TypeParameterSyntax> typeParameters)
        {
            context.WriteNewLine();
            context.WriteComment($"Method : {methodName}");

            TypeDeclarationVisitor.EnsureForwardedTypeDefinition(context, methodSymbol.ReturnType, []);
            var ilContext = context.ApiDriver.NewIlContext(context, simpleName, methodVar);
            
            var declaringTypeVarName = context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;
            var parameterSymbols = parameters.Select(p => context.SemanticModel.GetDeclaredSymbol(p)).ToArray();
            var exps = context.ApiDefinitionsFactory.Method(context, methodSymbol, new MemberDefinitionContext(methodVar, declaringTypeVarName, ilContext), methodName, methodModifiers, parameterSymbols, typeParameters);
            AddCecilExpressions(context, exps);

            //TODO: Temporary setting ilVar until we change its type to IlContext...
            if (!methodSymbol.IsAbstract && methodSymbol.ContainingType.TypeKind != TypeKind.Interface && !methodSymbol.IsExtern)
            {
                ilVar = ilContext.VariableName;
            }

            HandleAttributesInTypeParameter(context, typeParameters);
        }

        protected virtual string GetSpecificModifiers() => null;

        private string MethodNameOf(MethodDeclarationSyntax method)
        {
            return DeclaredSymbolFor(method).Name;
        }
    }
}
