using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using FieldAttributes = Mono.Cecil.FieldAttributes;

#nullable enable
namespace Cecilifier.Core.AST
{
    internal class PropertyDeclarationVisitor : SyntaxWalkerBase
    {
        private static readonly List<ParameterSpec> NoParameters = new();

        public PropertyDeclarationVisitor(IVisitorContext context) : base(context) { }
        
        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            if (PropertyAlreadyProcessed(node))
                return;

            using var _ = LineInformationTracker.Track(Context, node);
            Context.WriteNewLine();
            Context.WriteComment("** Property indexer **");

            var propertyType = ResolveType(node.Type, ResolveTargetKind.ReturnType);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
            var propName = "Item";

            AddDefaultMemberAttribute(propertyDeclaringTypeVar.VariableName, propName);

            var propertyParameters = new List<ParameterSpec>();
            foreach (var parameter in node.ParameterList.Parameters)
            {
                var paramSymbol = Context.SemanticModel.GetDeclaredSymbol(parameter).EnsureNotNull<ISymbol, IParameterSymbol>();
                propertyParameters.Add(
                    new ParameterSpec(
                        parameter.Identifier.Text, 
                        ResolveType(parameter.Type, ResolveTargetKind.Parameter),
                        paramSymbol.RefKind,
                        paramSymbol.AsParameterAttribute(),
                        parameter.Accept(DefaultParameterExtractorVisitor.Instance))
                    {
                        RegistrationTypeName = paramSymbol.Type.ToDisplayString()
                    });
            }

            var propDefVar = AddPropertyDefinition(node, propertyDeclaringTypeVar.VariableName, propertyDeclaringTypeVar.MemberName, propName, propertyParameters, propertyType);
            var backingFieldVar = ProcessPropertyAccessors(node, propertyDeclaringTypeVar.VariableName, propDefVar, propName, propertyParameters, node.ExpressionBody);

            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar, VariableMemberKind.None); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.FieldKeyword, backingFieldVar ?? String.Empty, VariableMemberKind.Field); // [field: attr], i.e, attr belongs to the backing field.
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (PropertyAlreadyProcessed(node))
                return;

            using var _ = LineInformationTracker.Track(Context, node);
            Context.WriteNewLine();
            Context.WriteComment($"** Property: {node.Identifier} **");
            var propertyType = ResolveType(node.Type, ResolveTargetKind.Parameter);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
            var propName = node.Identifier.ValueText;

            var propDefVar = AddPropertyDefinition(node, propertyDeclaringTypeVar.VariableName, propertyDeclaringTypeVar.MemberName, propName, [], propertyType);
            var backingFieldVar = ProcessPropertyAccessors(node, propertyDeclaringTypeVar.VariableName, propDefVar, node.Identifier.ValueText, NoParameters, node.ExpressionBody);

            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar, VariableMemberKind.None); // Normal property attrs
            // Attributes targeting backing field is only valid on auto-properties.
            if (node.AccessorList?.Accessors.All(a => a.Body == null && a.ExpressionBody == null) == true)
                HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.FieldKeyword, backingFieldVar ?? string.Empty, VariableMemberKind.Field); // [field: attr], i.e, attr belongs to the backing field.
        }

        private bool PropertyAlreadyProcessed(BasePropertyDeclarationSyntax node)
        {
            var propInfo = (IPropertySymbol?) Context.SemanticModel.GetDeclaredSymbol(node);
            if (propInfo == null)
                return false;

            // check the methods of the property because we do not register the property itself, only its methods. 
            var methodToCheck = propInfo.GetMethod ?? propInfo.SetMethod;
            var found = Context.DefinitionVariables.GetMethodVariable(methodToCheck.AsMethodDefinitionVariable());
            return found.IsValid;
        }

        private void AddDefaultMemberAttribute(string declaringTypeDefinitionVar, string value)
        {
            if (AttributeAlreadyAddedForAnotherMember())
                return;
            
            var exps = Context.ApiDefinitionsFactory.Attribute(
                                                    Context, 
                                                    Context.RoslynTypeSystem.ForType<DefaultMemberAttribute>().Ctor(Context.RoslynTypeSystem.SystemString), 
                                                    "defaultMember", 
                                                    declaringTypeDefinitionVar,
                                                    VariableMemberKind.Type,
                                                    new CustomAttributeArgument { Value =value });
            Context.Generate(exps);
            
            bool AttributeAlreadyAddedForAnotherMember()
            {
                var defaultMemberTrackerFlagName = $"{declaringTypeDefinitionVar}-{Constants.ContextFlags.DefaultMemberTracker}";
                var ret = Context.TryGetFlag(defaultMemberTrackerFlagName, out _);
                if (!ret)
                    Context.SetFlag(defaultMemberTrackerFlagName);
                return ret;
            }
        }

        private string? ProcessPropertyAccessors(BasePropertyDeclarationSyntax node, string propertyDeclaringTypeVar, string propDefVar, string propertyName, List<ParameterSpec> parameters, ArrowExpressionClauseSyntax? arrowExpression)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var propertySymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull<ISymbol, IPropertySymbol>();

            PropertyGenerator generator = new (Context);
            var propertyGenerationData = new PropertyGenerationData(
                                    propertySymbol.ContainingType.ToDisplayString(),
                                    propertyDeclaringTypeVar,
                                    propertySymbol.ContainingSymbol is INamedTypeSymbol { IsGenericType: true} && propertySymbol.IsDefinedInCurrentAssembly(Context),
                                    propDefVar,
                                    propertyName,
                                    AccessorsModifiersFor(node, propertySymbol),
                                    propertySymbol.IsStatic,
                                    resolveTargetKind => ResolveType(node.Type, resolveTargetKind),
                                    propertySymbol.Type.ToDisplayString(),
                                    parameters,
                                    BackingFieldModifiersFor(node), 
                                    propertySymbol.StoreOpCodeForFieldAccess(),
                                    propertySymbol.LoadOpCodeForFieldAccess());

            if (arrowExpression != null)
            {
                AddExpressionBodiedGetterMethod();
                return generator.BackingFieldVariable;
            }
           
            foreach (var accessor in node.AccessorList!.Accessors)
            {
                Context.WriteNewLine();
                switch (accessor.Keyword.Kind())
                {
                    case SyntaxKind.GetKeyword:
                        AddGetterMethod(accessor);
                        break;

                    case SyntaxKind.InitKeyword:
                    case SyntaxKind.SetKeyword:
                        Context.WriteComment($" {(accessor.Keyword.IsKind(SyntaxKind.InitKeyword) ? "Init": "Setter")}");
                        AddSetterMethod(propertySymbol, accessor);
                        break;
                    default:
                        throw new NotImplementedException($"Accessor {propertyName}.{accessor.Keyword} not support.");
                }
            }

            return generator.BackingFieldVariable;

            void AddSetterMethod(IPropertySymbol property, AccessorDeclarationSyntax accessor)
            {
                var setMethodVar = Context.Naming.SyntheticVariable("set", ElementKind.LocalVariable);
                var ilContext = Context.ApiDriver.NewIlContext(Context, "set", setMethodVar);
                using var methodVariableScope = generator.AddSetterMethodDeclaration(
                                                                    in propertyGenerationData,
                                                                    setMethodVar,
                                                                    accessor.IsKind(SyntaxKind.InitAccessorDeclaration),
                                                                    property.SetMethod!.ToDisplayString(),
                                                                    GetOverridenMethod(propertySymbol.SetMethod),
                                                                    ilContext);
                
                if (propertySymbol.ContainingType.TypeKind == TypeKind.Interface)
                    return;

                if (accessor.Body == null && accessor.ExpressionBody == null) //is this an auto property ?
                {
                    generator.AddAutoSetterMethodImplementation(in propertyGenerationData, ilContext);
                }
                else if (accessor.Body != null)
                {
                    StatementVisitor.Visit(Context, ilContext, accessor.Body);
                }
                else
                {
                    ExpressionVisitor.Visit(Context, ilContext, accessor.ExpressionBody!);
                }

                Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Ret);
            }

            ScopedDefinitionVariable AddGetterMethodGuts(string getMethodVar, out IlContext? ilContext)
            {
                Context.WriteComment("Getter");
                var il = propertySymbol.ContainingType.TypeKind != TypeKind.Interface 
                                                            ? Context.ApiDriver.NewIlContext(Context, "get", getMethodVar) 
                                                            : null;
                
                var methodVariableScope = generator.AddGetterMethodDeclaration(
                                                        in propertyGenerationData, 
                                                        getMethodVar, 
                                                        propertySymbol.HasCovariantGetter(),
                                                        propertySymbol.GetMethod!.ToDisplayString(), 
                                                        GetOverridenMethod(propertySymbol.GetMethod),
                                                        il);

                ilContext = propertySymbol.ContainingType.TypeKind != TypeKind.Interface ? il : null;
                return methodVariableScope;
            }

            void AddExpressionBodiedGetterMethod()
            {
                var getMethodVar = Context.Naming.SyntheticVariable("get", ElementKind.Method);
                using var getterMethodScope = AddGetterMethodGuts(getMethodVar, out var ilVar);
                Debug.Assert(ilVar != null);
                ProcessExpressionBodiedGetter(ilVar, arrowExpression);
            }

            void AddGetterMethod(AccessorDeclarationSyntax accessor)
            {
                var getMethodVar = Context.Naming.SyntheticVariable("get", ElementKind.Method);
                using var getterMethodScope = AddGetterMethodGuts(getMethodVar, out var ilVar);
                if (ilVar == null)
                    return;

                if (accessor.Body == null && accessor.ExpressionBody == null) //is this an auto property ?
                {
                    generator.AddAutoGetterMethodImplementation(ref propertyGenerationData, ilVar, getMethodVar);
                }
                else if (accessor.Body != null)
                {
                    StatementVisitor.Visit(Context, ilVar, accessor.Body);
                }
                else
                {
                    ProcessExpressionBodiedGetter(ilVar, accessor.ExpressionBody);
                }
            }

            void ProcessExpressionBodiedGetter(string ilVar, ArrowExpressionClauseSyntax? expression)
            {
                ExpressionVisitor.Visit(Context, ilVar, expression!);
                Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ret);
            }
        }

        private IDictionary<string,string?> AccessorsModifiersFor(BasePropertyDeclarationSyntax node, IPropertySymbol propertySymbol)
        {
            if (node.AccessorList == null)
            {
                Debug.Assert(propertySymbol.GetMethod != null);
                var accessorModifiers = node.Modifiers.MethodModifiersToCecil(Constants.Cecil.MethodAttributesSpecialName, propertySymbol.GetMethod);
                return new Dictionary<string, string?>()
                {
                    ["get"] = accessorModifiers
                };
            }

            return new Dictionary<string, string?>
            {
                ["get"] = AccessorModifiers(SyntaxKind.GetAccessorDeclaration), 
                ["set"] = AccessorModifiers(SyntaxKind.SetAccessorDeclaration) ?? AccessorModifiers(SyntaxKind.InitAccessorDeclaration)
            };
            
            string? AccessorModifiers(SyntaxKind accessorKind)
            {
                var accessor = node.AccessorList.Accessors.SingleOrDefault(a => a.IsKind(accessorKind));
                if (accessor == null)
                    return null;
                
                var modifiers = accessor.Modifiers.Any() 
                    ? node.ModifiersExcludingAccessibility().Concat(accessor.Modifiers) 
                    : node.Modifiers;
                    
                var accessorSymbol = accessorKind == SyntaxKind.GetAccessorDeclaration ? propertySymbol.GetMethod : propertySymbol.SetMethod;
                return modifiers.MethodModifiersToCecil(Constants.Cecil.MethodAttributesSpecialName, accessorSymbol);
            }
        }

        private static string BackingFieldModifiersFor(BasePropertyDeclarationSyntax node)
        {
            var m = node.Modifiers.ExceptBy(
                [SyntaxKind.PublicKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.InternalKeyword, SyntaxKind.VirtualKeyword, SyntaxKind.OverrideKeyword, SyntaxKind.PartialKeyword],
                c => c.Kind());
            
            if (node.AccessorList != null && node.AccessorList.Accessors.Any(acc => acc.IsKind(SyntaxKind.InitAccessorDeclaration)))
                m = m.Append(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)); // properties with `init` accessors are considered `readonly`

            return ModifiersToCecil<FieldAttributes>(m, "Private", FieldDeclarationVisitor.MapFieldAttributesFor);
        }

        private string AddPropertyDefinition(BasePropertyDeclarationSyntax propertyDeclarationSyntax, string declaringTypeVariable, string declaringTypeName, string propName, List<ParameterSpec> propertyParameters, string propertyType)
        {
            var propDefVar = Context.Naming.PropertyDeclaration(propertyDeclarationSyntax);
            var mdc = new BodiedMemberDefinitionContext(propName, propDefVar, declaringTypeVariable, propertyDeclarationSyntax.IsStatic() ?  MemberOptions.Static : MemberOptions.None, IlContext.None);
            var exps = Context.ApiDefinitionsFactory.Property(Context, mdc, declaringTypeName, propertyParameters, propertyType);
            Context.Generate(exps);
            
            return propDefVar;
        }
    }
}
