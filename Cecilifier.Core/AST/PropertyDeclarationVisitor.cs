using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using static Cecilifier.Core.Misc.Utils;

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

            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;
            var propName = "Item";

            AddDefaultMemberAttribute(propertyDeclaringTypeVar, propName);
            var propDefVar = AddPropertyDefinition(node, propName, propertyType);

            var paramsVar = new List<ParameterSpec>();
            foreach (var parameter in node.ParameterList.Parameters)
            {
                var paramSymbol = Context.SemanticModel.GetDeclaredSymbol(parameter).EnsureNotNull<ISymbol, IParameterSymbol>();
                paramsVar.Add(
                    new ParameterSpec(
                        parameter.Identifier.Text, 
                        ResolveType(parameter.Type),
                        paramSymbol.RefKind,
                        paramSymbol.AsParameterAttribute(),
                        parameter.Accept(DefaultParameterExtractorVisitor.Instance))
                    {
                        RegistrationTypeName = paramSymbol.Type.ToDisplayString()
                    });
            }

            var backingFieldVar = ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propDefVar, propName, paramsVar, node.ExpressionBody);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");

            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.FieldKeyword, backingFieldVar ?? String.Empty); // [field: attr], i.e, attr belongs to the backing field.
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (PropertyAlreadyProcessed(node))
                return;

            using var _ = LineInformationTracker.Track(Context, node);
            Context.WriteNewLine();
            Context.WriteComment($"** Property: {node.Identifier} **");
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;
            var propName = node.Identifier.ValueText;

            var propDefVar = AddPropertyDefinition(node, propName, propertyType);
            var backingFieldVar = ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propDefVar, node.Identifier.ValueText, NoParameters, node.ExpressionBody);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");

            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            // Attributes targeting backing field is only valid on auto-properties.
            if (node.AccessorList?.Accessors.All(a => a.Body == null && a.ExpressionBody == null) == true)
                HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.FieldKeyword, backingFieldVar ?? string.Empty); // [field: attr], i.e, attr belongs to the backing field.
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
            
            var ctorVar = Context.Naming.MemberReference("ctor");
            var customAttrVar = Context.Naming.CustomAttribute("DefaultMember");

            var exps = new[]
            {
                $"var {ctorVar} = {ImportFromMainModule("typeof(System.Reflection.DefaultMemberAttribute).GetConstructor(new Type[] { typeof(string) })")};",
                $"var {customAttrVar} = new CustomAttribute({ctorVar});",
                $"{customAttrVar}.ConstructorArguments.Add(new CustomAttributeArgument({Context.TypeResolver.Bcl.System.String}, \"{value}\"));",
                $"{declaringTypeDefinitionVar}.CustomAttributes.Add({customAttrVar});"
            };

            AddCecilExpressions(Context, exps);
            
            bool AttributeAlreadyAddedForAnotherMember()
            {
                var defaultMemberTrackerFlagName = $"{declaringTypeDefinitionVar}-{Constants.ContextFlags.DefaultMemberTracker}";
                var ret = Context.TryGetFlag(defaultMemberTrackerFlagName, out var _);
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
                                    ResolveType(node.Type),
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
                        throw new NotImplementedException($"Accessor: {accessor.Keyword}");
                }
            }

            return generator.BackingFieldVariable;

            void AddSetterMethod(IPropertySymbol property, AccessorDeclarationSyntax accessor)
            {
                var setMethodVar = Context.Naming.SyntheticVariable("set", ElementKind.LocalVariable);
                using var methodVariableScope = generator.AddSetterMethodDeclaration(
                                                                    in propertyGenerationData,
                                                                    setMethodVar,
                                                                    accessor.IsKind(SyntaxKind.InitAccessorDeclaration),
                                                                    property.SetMethod!.ToDisplayString(),
                                                                    GetOverridenMethod(propertySymbol.SetMethod));
                
                if (propertySymbol.ContainingType.TypeKind == TypeKind.Interface)
                    return;

                var ilContext = Context.ApiDriver.NewIlContext(Context, "set", setMethodVar);
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

                Context.ApiDriver.EmitCilInstruction(Context, ilContext, OpCodes.Ret);
            }

            ScopedDefinitionVariable AddGetterMethodGuts(string getMethodVar, out string? ilVar)
            {
                Context.WriteComment("Getter");
                var methodVariableScope = generator.AddGetterMethodDeclaration(
                                                        in propertyGenerationData, 
                                                        getMethodVar, 
                                                        propertySymbol.HasCovariantGetter(),
                                                        propertySymbol.GetMethod!.ToDisplayString(), 
                                                        GetOverridenMethod(propertySymbol.GetMethod));

                if (propertySymbol.ContainingType.TypeKind != TypeKind.Interface)
                {
                    ilVar = Context.Naming.ILProcessor("get");
                    AddCecilExpression($"var {ilVar} = {getMethodVar}.Body.GetILProcessor();");
                }
                else
                {
                    ilVar = null;
                }
                
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
                Context.EmitCilInstruction(ilVar, OpCodes.Ret);
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

        private string AddPropertyDefinition(BasePropertyDeclarationSyntax propertyDeclarationSyntax, string propName, string propertyType)
        {
            var propDefVar = Context.Naming.PropertyDeclaration(propertyDeclarationSyntax);
            var exps = CecilDefinitionsFactory.PropertyDefinition(propDefVar, propName, propertyType);
            Context.Generate(exps);
            
            return propDefVar;
        }
    }
}
