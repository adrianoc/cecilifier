using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class TypeDeclarationVisitor : SyntaxWalkerBase
    {
        public TypeDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            var definitionVar = HandleInterfaceDeclaration(node);
            using (Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, definitionVar))
            {
                base.VisitInterfaceDeclaration(node);
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var definitionVar = HandleTypeDeclaration(node);
            using (Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, definitionVar))
            {
                base.VisitClassDeclaration(node);
            }
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var definitionVar = HandleTypeDeclaration(node);
            using (Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, definitionVar))
            {
                base.VisitStructDeclaration(node);
            }
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            new FieldDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            new PropertyDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            new PropertyDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            new ConstructorDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            new MethodDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            new EventDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            new EventDeclarationVisitor(Context).Visit(node);
        }

        private string ProcessBase(TypeDeclarationSyntax classDeclaration)
        {
            var classSymbol = DeclaredSymbolFor(classDeclaration);

            var resolvedBaseType = Context.TypeResolver.ResolveTypeLocalVariable(classSymbol.BaseType.Name) ?? Context.TypeResolver.Resolve(classSymbol.BaseType);
            if (classSymbol.BaseType.IsGenericType && classSymbol.BaseType.IsDefinedInCurrentType(Context))
            {
                resolvedBaseType = $"{resolvedBaseType}.MakeGenericInstanceType({classSymbol.BaseType.TypeArguments.Select(ResolveTypeArgument).Aggregate((acc, curr) => acc + "," + curr)})";
            }
            
            return resolvedBaseType;
        }

        private string ResolveTypeArgument(ITypeSymbol typeArg)
        {
            if (typeArg is ITypeParameterSymbol typeParameterSymbol && Context.TypeResolver.ResolveTypeLocalVariable(typeArg.Name) == null)
            {
                // We need to forward "declare" the variable....
                return CecilDefinitionsFactory.GenericParameter(Context, $"tv_{typeParameterSymbol.Name}_{typeParameterSymbol.Ordinal}", typeArg.Name, "", typeParameterSymbol);
            }

            return Context.TypeResolver.Resolve(typeArg);
        }

        private IEnumerable<string> ImplementedInterfacesFor(BaseListSyntax bases)
        {
            if (bases == null)
            {
                yield break;
            }

            foreach (var @base in bases.Types)
            {
                var info = Context.GetTypeInfo(@base.Type);
                if (info.Type.TypeKind == TypeKind.Interface)
                {
                    var itfFQName = @base.DescendantTokens().OfType<SyntaxToken>().Aggregate("", (acc, curr) => acc + curr.ValueText);
                    yield return itfFQName;
                }
            }
        }

        private string HandleInterfaceDeclaration(TypeDeclarationSyntax node)
        {
            return HandleTypeDeclaration(node, null);
        }

        private string HandleTypeDeclaration(TypeDeclarationSyntax node)
        {
            return HandleTypeDeclaration(node, Context.TypeResolver.ResolvePredefinedType("Object"));
        }

        private string HandleTypeDeclaration(TypeDeclarationSyntax node, string baseType)
        {
            var varName = LocalVariableNameForId(NextLocalVariableTypeId());
            var isStructWithNoFields = node.Kind() == SyntaxKind.StructDeclaration && node.Members.Count == 0;
            var typeDefinitionExp = CecilDefinitionsFactory.Type(
                                            Context, 
                                            varName,
                                            node.Identifier.ValueText, 
                                            TypeModifiersToCecil(node), 
                                            baseType, 
                                            isStructWithNoFields, 
                                            ImplementedInterfacesFor(node.BaseList).Select(i => Context.TypeResolver.Resolve(i)),
                                            node.TypeParameterList);
            
            AddCecilExpressions(typeDefinitionExp);

            if (baseType != null)
            {
                // we postpone setting the base type because it may depend on generic parameters defined in the class itself (for instance 'class C<T> : Base<T> {}')
                // and these are introduced by the code in CecilDefinitionsFactory.Type().
                WriteCecilExpression(Context, $"{varName}.BaseType = {ProcessBase(node)};");
            }

            EnsureCurrentTypeHasADefaultCtor(node, varName);

            HandleAttributesInMemberDeclaration(node, varName);
            
            Context.WriteCecilExpression(Environment.NewLine);

            return varName;
        }

        private void EnsureCurrentTypeHasADefaultCtor(TypeDeclarationSyntax node, string typeLocalVar)
        {
            node.Accept(new DefaultCtorVisitor(Context, typeLocalVar));
        }
    }

    internal class DefaultCtorVisitor : CSharpSyntaxWalker
    {
        private readonly IVisitorContext context;

        private bool defaultCtorFound;
        private readonly string localVarName;

        public DefaultCtorVisitor(IVisitorContext context, string localVarName)
        {
            this.localVarName = localVarName;
            this.context = context;
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            foreach (var member in NonTypeMembersOf(node))
            {
                member.Accept(this);
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            foreach (var member in NonTypeMembersOf(node))
            {
                member.Accept(this);
            }

            if (!defaultCtorFound)
            {
                new ConstructorDeclarationVisitor(context).DefaultCtorInjector(localVarName, node);
            }
        }

        private static IEnumerable<MemberDeclarationSyntax> NonTypeMembersOf(TypeDeclarationSyntax node)
        {
            return node.Members.Where(m => m.Kind() != SyntaxKind.ClassDeclaration && m.Kind() != SyntaxKind.StructDeclaration && m.Kind() != SyntaxKind.EnumDeclaration);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax ctorNode)
        {
            // bailout in case the ctor has parameters or is static (a cctor)
            if (ctorNode.ParameterList.Parameters.Count > 0 || ctorNode.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)))
            {
                return;
            }

            defaultCtorFound = true;
        }
    }
}
