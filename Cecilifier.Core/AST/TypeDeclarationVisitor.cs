using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST.MemberDependencies;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal partial class TypeDeclarationVisitor : SyntaxWalkerBase
    {
        public TypeDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var definitionVar = Context.Naming.Type(node);
            var interfaceSymbol = Context.SemanticModel.GetDeclaredSymbol(node);
            using (Context.DefinitionVariables.WithCurrent(interfaceSymbol.ContainingSymbol.ToDisplayString(), interfaceSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, definitionVar))
            {
                HandleTypeDeclaration(node, definitionVar);
                ProcessMembers(node);
                base.VisitInterfaceDeclaration(node);
            }
            Context.OnFinishedTypeDeclaration(interfaceSymbol);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var definitionVar = Context.Naming.Type(node);
            var classSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull();
            var found = Context.DefinitionVariables.GetVariable(classSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, classSymbol.ContainingType?.OriginalDefinition.ToDisplayString());
            if (found.IsValid && found.IsForwarded)
                definitionVar = found.VariableName;
            
            using (Context.DefinitionVariables.WithCurrent(classSymbol.ContainingSymbol.OriginalDefinition.ToDisplayString(), classSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, definitionVar))
            {
                ProcessTypeDeclaration(node, definitionVar);
            }
            Context.OnFinishedTypeDeclaration(classSymbol);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var definitionVar = Context.Naming.Type(node);
            var structSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull();
            using (Context.DefinitionVariables.WithCurrent(structSymbol.ContainingSymbol.OriginalDefinition.ToDisplayString(), structSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, definitionVar))
            {
                ProcessTypeDeclaration(node, definitionVar);
                ProcessStructPseudoAttributes(definitionVar, structSymbol);
            }
            Context.OnFinishedTypeDeclaration(structSymbol);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var definitionVar = Context.Naming.Type(node);
            var recordSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull();
            using var variable = Context.DefinitionVariables.WithCurrent(recordSymbol.ContainingSymbol.ToDisplayString(), recordSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, definitionVar);
            ProcessTypeDeclaration(node, definitionVar);

            RecordGenerator generator = new(Context, definitionVar, node);
            generator.AddNullabilityAttributesToTypeDefinition(definitionVar);
            generator.AddSyntheticMembers();
            Context.OnFinishedTypeDeclaration(recordSymbol);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var enumSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull<ISymbol, INamedTypeSymbol>($"Something really bad happened. Roslyn failed to resolve the symbol for the enum {node.Identifier.Text}");
            node.Accept(new EnumDeclarationVisitor(Context, enumSymbol));
            Context.OnFinishedTypeDeclaration(enumSymbol);
        }

        void ProcessTypeDeclaration(TypeDeclarationSyntax node, string definitionVar)
        {
            HandleTypeDeclaration(node, definitionVar);
            foreach (var innerType in node.Members.OfType<BaseTypeDeclarationSyntax>())
            {
                innerType.Accept(this);
            }
            ProcessMembers(node);
            EnsureCurrentTypeHasADefaultCtor(node, definitionVar);
        }
        
        /// <summary>
        /// In order to generate more readable cecilified code, sort a type's members
        /// by dependency and process then so any referenced members have a higher
        /// chance to be handled before the referrer.
        ///
        /// For instance, given the type:
        /// <code>
        /// class Foo
        /// {
        ///     void M1() { M2(); }
        ///     void M2() {}
        /// }
        /// </code>
        /// process M2() and M1() in this order.
        ///
        /// If we don´t sort and rely on the order the members appears in the code
        /// then the order of processing would be M1() and then M2(); the problem
        /// with that is since M1() has a call (reference) to M2() while generating
        /// M1() body's Cecilifier would emmit the code to include a <see cref="Mono.Cecil.MethodDefinition"/>
        /// for M2() and then, after finishing the code for M1() emit the rest of
        /// M2() method. Mixing code related to members like this turns the code
        /// harder to reason about.
        ///
        /// </summary>
        /// <param name="node">Type declaration node with members to be visited</param>
        private void ProcessMembers(TypeDeclarationSyntax node)
        {
            var depCollector = new MemberDependencyCollector<MemberDependency>();
            var members  = depCollector.Process(node, Context.SemanticModel);

            var visitor = new ForwardMemberReferenceAvoidanceVisitor(new MemberDeclarationVisitor(Context));
            foreach (var member in members)
            {
                member.Accept(visitor);
            }
        }
        
        private ResolvedType ProcessBase(TypeDeclarationSyntax classDeclaration)
        {
            var classSymbol = DeclaredSymbolFor(classDeclaration);
            return Context.TypeResolver.ResolveAny(classSymbol.BaseType);
        }

        private void HandleTypeDeclaration(TypeDeclarationSyntax node, string varName)
        {
            var typeSymbol = DeclaredSymbolFor(node);

            var found = Context.DefinitionVariables.GetVariable(typeSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, typeSymbol.ContainingSymbol?.ToDisplayString());
            if (!found.IsValid || !found.IsForwarded)
            {
                AddTypeDefinition(Context, varName, typeSymbol, node.Modifiers, node.TypeParameterList?.Parameters, node.CollectOuterTypeArguments());
            }

            if (typeSymbol.BaseType?.IsGenericType == true)
            {
                // we postpone setting the base type because it may depend on generic parameters defined in the class itself (for instance 'class C<T> : Base<T> {}')
                // and these are introduced by the code in CecilDefinitionsFactory.Type().
                WriteCecilExpression(Context, $"{varName}.BaseType = {ProcessBase(node)};");
            }

            HandleAttributesInMemberDeclaration(node.AttributeLists, varName, VariableMemberKind.Type);

            NonCapturingLambdaProcessor.InjectSyntheticMethodsForNonCapturingLambdas(Context, node, varName);

            Context.WriteNewLine();
            Context.ClearFlag($"{varName}-{Constants.ContextFlags.DefaultMemberTracker}");
        }

        internal static void EnsureForwardedTypeDefinition(IVisitorContext context, ITypeSymbol typeSymbol, IEnumerable<TypeParameterSyntax> typeParameters)
        {
            if (typeSymbol.TypeKind == TypeKind.TypeParameter)
                return;

            if (!typeSymbol.IsDefinedInCurrentAssembly(context))
                goto processGenerics;

            var found = context.DefinitionVariables.GetVariable(typeSymbol.OriginalDefinition.ToDisplayString(), VariableMemberKind.Type, typeSymbol.ContainingSymbol?.OriginalDefinition.ToDisplayString());
            if (found.IsValid)
                goto processGenerics;

            var typeDeclaration = (MemberDeclarationSyntax) typeSymbol.DeclaringSyntaxReferences.First().GetSyntax();
            var typeDeclarationVar = context.Naming.Type(typeSymbol.Name, typeSymbol.TypeKind.ToElementKind());
            AddTypeDefinition(context, typeDeclarationVar, typeSymbol, typeDeclaration.Modifiers, typeParameters, []);

            var v = context.DefinitionVariables.RegisterNonMethod(
                                        typeSymbol.ContainingSymbol?.OriginalDefinition.ToDisplayString(),
                                        typeSymbol.OriginalDefinition.ToDisplayString(),
                                        VariableMemberKind.Type,
                                        typeDeclarationVar);
            v.IsForwarded = true;

        processGenerics:
            if (typeSymbol is INamedTypeSymbol genericType)
            {
                foreach (var typeArgument in genericType.TypeArguments)
                {
                    EnsureForwardedTypeDefinition(context, typeArgument, Array.Empty<TypeParameterSyntax>());
                }
            }
        }

        private static void AddTypeDefinition(IVisitorContext context, string typeDeclarationVar, ITypeSymbol typeSymbol, SyntaxTokenList typeModifiers, IEnumerable<TypeParameterSyntax> typeParameters, IEnumerable<TypeParameterSyntax> outerTypeParameters)
        {
            context.WriteNewLine();
            context.WriteComment($"{(typeSymbol.IsRecord ? "Record ": string.Empty)}{typeSymbol.TypeKind} : {typeSymbol.Name}");

            typeParameters ??= [];

            var outerTypeVariable = context.DefinitionVariables.GetVariable(typeSymbol.ContainingType?.ToDisplayString(), VariableMemberKind.Type, typeSymbol.ContainingType?.ContainingSymbol.ToDisplayString());
            var isStructWithNoFields = typeSymbol.TypeKind == TypeKind.Struct && typeSymbol.GetMembers().Length == 0;
            var typeDefinitionExp = context.ApiDefinitionsFactory.Type(
                context,
                new MemberDefinitionContext(typeSymbol.Name, typeDeclarationVar, outerTypeVariable.IsValid ? outerTypeVariable.VariableName : null),
                typeSymbol.ContainingNamespace?.FullyQualifiedName() ?? string.Empty,
                context.ApiDefinitionsFactory.MappedTypeModifiersFor((INamedTypeSymbol)typeSymbol, typeModifiers),
                BaseTypeFor(context, typeSymbol),
                isStructWithNoFields,
                typeSymbol.Interfaces,
                typeParameters,
                outerTypeParameters);

            AddCecilExpressions(context, typeDefinitionExp);

            HandleAttributesInTypeParameter(context, typeParameters);
        }

        private static ResolvedType BaseTypeFor(IVisitorContext context, ITypeSymbol typeSymbol)
        {
            if (typeSymbol.BaseType == null)
                return null;

            EnsureForwardedTypeDefinition(context, typeSymbol.BaseType, []);

            return typeSymbol.BaseType.IsGenericType ? default : context.TypeResolver.ResolveAny(typeSymbol.BaseType);
        }

        private void EnsureCurrentTypeHasADefaultCtor(TypeDeclarationSyntax node, string typeLocalVar)
        {
            node.Accept(new DefaultCtorVisitor(Context, typeLocalVar));
        }

        private void ProcessStructPseudoAttributes(string structDefinitionVar, INamedTypeSymbol structSymbol)
        {
            if (structSymbol.IsReadOnly)
            {
                var ctor = Context.RoslynTypeSystem.IsReadOnlyAttribute.ParameterlessCtor();
                var attrExps = Context.ApiDefinitionsFactory.Attribute(Context, ctor, structSymbol.Name, structDefinitionVar, VariableMemberKind.Type);
                Context.Generate(attrExps);
            }

            if (structSymbol.IsRefLikeType)
            {
                var ctor = Context.RoslynTypeSystem.IsByRefLikeAttribute.ParameterlessCtor();
                var exps = Context.ApiDefinitionsFactory.Attribute(Context, ctor, structSymbol.Name, structDefinitionVar, VariableMemberKind.Type);
                Context.Generate(exps);
                
                var obsoleteAttrCtor = Context.RoslynTypeSystem.SystemObsoleteAttribute.Ctor(Context.RoslynTypeSystem.SystemString, Context.RoslynTypeSystem.SystemBoolean);
                const string RefStructObsoleteMsg = "Types with embedded references are not supported in this version of your compiler.";
                exps = Context.ApiDefinitionsFactory.Attribute(Context, obsoleteAttrCtor, "obsolete", structDefinitionVar, VariableMemberKind.Type,
                                            new CustomAttributeArgument { Value = RefStructObsoleteMsg}, 
                                            new CustomAttributeArgument { Value = true});
                Context.Generate(exps);
            }
        }
    }

    internal class DefaultCtorVisitor : CSharpSyntaxWalker
    {
        [Flags]
        enum ConstructorKind
        {
            Static = 0x1,
            Instance = 0x2
        }

        private readonly IVisitorContext context;

        private readonly string declaringTypeVarName;
        private ConstructorKind foundConstructors;
        private bool hasStaticInitialization;

        public DefaultCtorVisitor(IVisitorContext context, string declaringTypeVarName)
        {
            this.declaringTypeVarName = declaringTypeVarName;
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
            
            if ((foundConstructors & ConstructorKind.Instance) != ConstructorKind.Instance)
            {
                new ConstructorDeclarationVisitor(context).DefaultCtorInjector(declaringTypeVarName, node, false);
            }

            if ((foundConstructors & ConstructorKind.Static) != ConstructorKind.Static && hasStaticInitialization)
            {
                new ConstructorDeclarationVisitor(context).DefaultCtorInjector(declaringTypeVarName, node, true);
            }
        }

        private static IEnumerable<MemberDeclarationSyntax> NonTypeMembersOf(TypeDeclarationSyntax node)
        {
            return node.Members.Where(m => m.Kind() != SyntaxKind.ClassDeclaration && m.Kind() != SyntaxKind.StructDeclaration && m.Kind() != SyntaxKind.EnumDeclaration && m.Kind() != SyntaxKind.InterfaceDeclaration);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax ctorNode)
        {
            foundConstructors |= ctorNode.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)) ? ConstructorKind.Static : ConstructorKind.Instance;
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) && node.Declaration.Variables.Any(v => v.Initializer != null))
                hasStaticInitialization = true;
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) && node.Initializer != null)
                hasStaticInitialization = true;
        }
    }
}
