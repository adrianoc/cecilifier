using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

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
            node.Accept(expressionVisitor);
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
                Context.Naming.MethodDeclaration(node),
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

            var paramVar = Context.Naming.Parameter(node);
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

        protected void ProcessMethodDeclaration<T>(T node, string variableName, string simpleName, string fqName, ITypeSymbol returnType, bool refReturn, Action<string> runWithCurrent, IList<TypeParameterSyntax> typeParameters = null) where T : BaseMethodDeclarationSyntax
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var declaringTypeName = DeclaringTypeFrom(node);
            using (Context.DefinitionVariables.EnterScope())
            {
                typeParameters = typeParameters ?? Array.Empty<TypeParameterSyntax>();

                var methodVar = AddOrUpdateMethodDefinition(node, variableName, fqName, node.Modifiers.MethodModifiersToCecil((targetEnum, modifiers, defaultAccessibility) => ModifiersToCecil(modifiers, targetEnum, defaultAccessibility), GetSpecificModifiers(), DeclaredSymbolFor(node)), returnType, refReturn, typeParameters);
                AddCecilExpression("{0}.Methods.Add({1});", Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName, methodVar);

                HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.ReturnKeyword, methodVar); // Normal method attrs.
                HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.ReturnKeyword, $"{methodVar}.MethodReturnType"); // [return:Attr]

                var isAbstract = DeclaredSymbolFor(node).IsAbstract;
                if (!isAbstract)
                {
                    ilVar = Context.Naming.ILProcessor(simpleName, declaringTypeName);
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

        private static string DeclaringTypeFrom<T>(T node) where T : BaseMethodDeclarationSyntax
        {
            var declaringType = (TypeDeclarationSyntax) node.Parent;
            Debug.Assert(declaringType != null);
            
            return declaringType.Identifier.Text;
        }

        private string AddOrUpdateMethodDefinition(BaseMethodDeclarationSyntax node, string variableName, string fqName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
        {
            var declaringTypeName = node.ResolveDeclaringType<BaseTypeDeclarationSyntax>().Identifier.Text;
            var found = Context.DefinitionVariables.GetMethodVariable(new MethodDefinitionVariable(declaringTypeName, fqName, node.ParameterList.Parameters.Select(paramSyntax => Context.GetTypeInfo(paramSyntax.Type).Type.Name).ToArray()));
            if (found.IsValid)
            {
                AddCecilExpression("{0}.Attributes = {1};", found.VariableName , methodModifiers);
                AddCecilExpression("{0}.HasThis = !{0}.IsStatic;", found.VariableName);
                return found.VariableName;
            }

            AddMethodDefinition(Context, variableName, fqName, methodModifiers, returnType, refReturn, typeParameters);
            return variableName;
        }

        public static void AddMethodDefinition(IVisitorContext context, string methodVar, string fqName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
        {
            context.WriteNewLine();
            context.WriteComment($"Method : {fqName}");

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
