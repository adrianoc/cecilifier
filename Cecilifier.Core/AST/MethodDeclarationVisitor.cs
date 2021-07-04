using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;
using static System.Environment; 

namespace Cecilifier.Core.AST
{
    internal class MethodDeclarationVisitor : SyntaxWalkerBase
    {
        protected string ilVar;

        public MethodDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            var expressionVisitor = new ExpressionVisitor(Context, ilVar);
            node.Expression.Accept(expressionVisitor);
        }

        public override void VisitBlock(BlockSyntax node)
        {
            StatementVisitor.Visit(Context, ilVar, node);
        }
        
        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var returnType = node.ReturnType switch
            {
                RefTypeSyntax refType => refType.Type,
                _ => node.ReturnType
            };
                
            ProcessMethodDeclaration(
                node, 
                node.Identifier.ValueText, 
                MethodNameOf(node), 
                Context.GetTypeInfo(returnType).Type,
                node.ReturnType is RefTypeSyntax,
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
            var declaringMethodName = ".ctor";

            var declaringMethodOrCtor = (BaseMethodDeclarationSyntax) node.Parent.Parent;

            var declaringType = declaringMethodOrCtor.ResolveDeclaringType<TypeDeclarationSyntax>();

            if (node.Parent.Parent.IsKind(SyntaxKind.MethodDeclaration))
            {
                var declaringMethod = (MethodDeclarationSyntax) declaringMethodOrCtor;
                declaringMethodName = declaringMethod.Identifier.ValueText;
            }

            var paramVar = TempLocalVar(node.Identifier.ValueText);
            Context.DefinitionVariables.RegisterNonMethod(string.Empty, node.Identifier.ValueText, MemberKind.Parameter, paramVar);

            var tbf = new MethodDefinitionVariable(
                declaringType.Identifier.Text,
                declaringMethodName,
                declaringMethodOrCtor.ParameterList.Parameters.Select(p => Context.GetTypeInfo(p.Type).Type.Name).ToArray());

            var declaringMethodVariable = Context.DefinitionVariables.GetMethodVariable(tbf).VariableName;

            var exps = CecilDefinitionsFactory.Parameter(node, Context.SemanticModel, declaringMethodVariable, paramVar, ResolveType(node.Type));
            AddCecilExpressions(exps);
            
            HandleAttributesInMemberDeclaration(node.AttributeLists, paramVar);

            base.VisitParameter(node);
        }

        protected void ProcessMethodDeclaration<T>(T node, string simpleName, string fqName, ITypeSymbol returnType, bool refReturn, Action<string> runWithCurrent, IList<TypeParameterSyntax> typeParameters = null) where T : BaseMethodDeclarationSyntax
        {
            var declaringTypeName = DeclaringTypeNameFor(node);
            var methodVar = MethodExtensions.LocalVariableNameFor(declaringTypeName, simpleName, node.MangleName(Context.SemanticModel));

            using (Context.DefinitionVariables.EnterScope())
            {
                typeParameters = typeParameters ?? Array.Empty<TypeParameterSyntax>();

                AddOrUpdateMethodDefinition(methodVar, fqName, node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, GetSpecificModifiers(), DeclaredSymbolFor(node)), returnType, refReturn, typeParameters);
                AddCecilExpression("{0}.Methods.Add({1});", Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName, methodVar);

                HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.ReturnKeyword, methodVar); // Normal method attrs.
                HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.ReturnKeyword, $"{methodVar}.MethodReturnType"); // [return:Attr]

                var isAbstract = DeclaredSymbolFor(node).IsAbstract;
                if (!isAbstract)
                {
                    ilVar = MethodExtensions.LocalVariableNameFor("il", declaringTypeName, simpleName, node.MangleName(Context.SemanticModel));
                    AddCecilExpression($"{methodVar}.Body.InitLocals = true;");
                    AddCecilExpression($"var {ilVar} = {methodVar}.Body.GetILProcessor();");
                }

                WithCurrentMethod(declaringTypeName, methodVar, fqName, node.ParameterList.Parameters.Select(p => Context.GetTypeInfo(p.Type).Type.Name).ToArray(), runWithCurrent);

                if (!isAbstract && !node.DescendantNodes().Any(n => n.IsKind(SyntaxKind.ReturnStatement)))
                {
                    AddCilInstruction(ilVar, OpCodes.Ret);
                }
            }
        }

        private static string DeclaringTypeNameFor<T>(T node) where T : BaseMethodDeclarationSyntax
        {
            var declaringType = (TypeDeclarationSyntax) node.Parent;
            return declaringType.Identifier.ValueText;
        }

        private void AddOrUpdateMethodDefinition(string methodVar, string fqName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
        {
            if (Context.Contains(methodVar))
            {
                AddCecilExpression("{0}.Attributes = {1};", methodVar, methodModifiers);
                AddCecilExpression("{0}.HasThis = !{0}.IsStatic;", methodVar);
            }
            else
            {
                AddMethodDefinition(Context, methodVar, fqName, methodModifiers, returnType, refReturn, typeParameters);
            }
        }

        public static void AddMethodDefinition(IVisitorContext context, string methodVar, string fqName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
        {
            context.WriteNewLine();
            context.WriteComment($"Method : {fqName}");

            context[methodVar] = "";
            var exps = CecilDefinitionsFactory.Method(context, methodVar, fqName, methodModifiers, returnType, refReturn, typeParameters);
            foreach(var exp in exps)
                context.WriteCecilExpression($"{exp}{NewLine}");
        }

        protected virtual string GetSpecificModifiers()
        {
            return null;
        }

        private string MethodNameOf(MethodDeclarationSyntax method)
        {
            return DeclaredSymbolFor(method).Name;
        }
    }
}
