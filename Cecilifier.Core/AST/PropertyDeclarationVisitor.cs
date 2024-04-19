using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.AST
{
    internal class PropertyDeclarationVisitor : SyntaxWalkerBase
    {
        private static readonly List<ParameterSpec> NoParameters = new();
        private string backingFieldVar;

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

            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, paramsVar, node.ExpressionBody);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");

            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.FieldKeyword, backingFieldVar); // [field: attr], i.e, attr belongs to the backing field.
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
            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, NoParameters, node.ExpressionBody);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");

            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches, SyntaxKind.FieldKeyword, backingFieldVar); // [field: attr], i.e, attr belongs to the backing field.
        }

        private bool PropertyAlreadyProcessed(BasePropertyDeclarationSyntax node)
        {
            var propInfo = (IPropertySymbol) Context.SemanticModel.GetDeclaredSymbol(node);
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

        private void ProcessPropertyAccessors(BasePropertyDeclarationSyntax node, string propertyDeclaringTypeVar, string propName, string propertyType, string propDefVar, List<ParameterSpec> parameters, ArrowExpressionClauseSyntax? arrowExpression)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            var propertySymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull<ISymbol, IPropertySymbol>();
            var accessorModifiers = node.Modifiers.MethodModifiersToCecil("MethodAttributes.SpecialName", propertySymbol.GetMethod ?? propertySymbol.SetMethod);

            if (arrowExpression != null)
            {
                AddExpressionBodiedGetterMethod(propertySymbol);
                return;
            }

            foreach (var accessor in node.AccessorList!.Accessors)
            {
                Context.WriteNewLine();
                switch (accessor.Keyword.Kind())
                {
                    case SyntaxKind.GetKeyword:
                        AddGetterMethod(accessor, propertySymbol);
                        break;

                    case SyntaxKind.InitKeyword:
                        Context.WriteComment(" Init");
                        var setterReturnType = $"new RequiredModifierType({Context.TypeResolver.Resolve(typeof(IsExternalInit).FullName)}, {Context.TypeResolver.Bcl.System.Void})";
                        AddSetterMethod(propertySymbol, setterReturnType, accessor);
                        break;

                    case SyntaxKind.SetKeyword:
                        Context.WriteComment(" Setter");
                        AddSetterMethod(propertySymbol, Context.TypeResolver.Bcl.System.Void, accessor);
                        break;
                    default:
                        throw new NotImplementedException($"Accessor: {accessor.Keyword}");
                }
            }

            void AddBackingFieldIfNeeded(AccessorDeclarationSyntax accessor, bool hasInitProperty)
            {
                if (backingFieldVar != null)
                    return;

                backingFieldVar = Context.Naming.FieldDeclaration(node);
                var m = accessor.Modifiers;
                if (hasInitProperty)
                    m = m.Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));

                if (propertySymbol.IsStatic)
                    m = m.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));

                var modifiers = ModifiersToCecil<FieldAttributes>(m, "Private", FieldDeclarationVisitor.MapFieldAttributesFor);
                var backingFieldExps = CecilDefinitionsFactory.Field(Context, propertySymbol.ContainingSymbol.ToDisplayString(), propertyDeclaringTypeVar, backingFieldVar, Utils.BackingFieldNameForAutoProperty(propName), propertyType, modifiers);
                AddCecilExpressions(Context, backingFieldExps);
            }

            void AddSetterMethod(IPropertySymbol property, string setterReturnType, AccessorDeclarationSyntax accessor)
            {
                var setMethodVar = Context.Naming.SyntheticVariable("set", ElementKind.LocalVariable);
                
                var completeParamList = new List<ParameterSpec>(parameters);
                
                // Setters always have at least one `value` parameter but Roslyn does not have it explicitly listed.
                completeParamList.Add(new ParameterSpec(
                    "value", 
                    Context.TypeResolver.Resolve(property.Type),
                    RefKind.None,
                    Constants.ParameterAttributes.None) { RegistrationTypeName = property.Type.ToDisplayString() } );

                var exps = CecilDefinitionsFactory.Method(
                    Context,
                    property.ContainingType.ToDisplayString(),
                    setMethodVar,
                    property.SetMethod!.ToDisplayString(),
                    $"set_{propName}",
                    $"{accessorModifiers}",
                    completeParamList, 
                    [], // Properties cannot declare TypeParameters
                    ctx => setterReturnType,
                    out var methodDefinitionVariable);

                using var _ = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
                Context.WriteCecilExpressions(exps);
                AddToOverridenMethodsIfAppropriated(setMethodVar, property.SetMethod);
                AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({setMethodVar});");
                
                var ilSetVar = Context.Naming.ILProcessor("set");

                AddCecilExpression($"{setMethodVar}.Body = new MethodBody({setMethodVar});");
                AddCecilExpression($"{propDefVar}.SetMethod = {setMethodVar};");

                AddCecilExpression($"var {ilSetVar} = {setMethodVar}.Body.GetILProcessor();");

                if (propertySymbol.ContainingType.TypeKind == TypeKind.Interface)
                    return;

                if (accessor.Body == null && accessor.ExpressionBody == null) //is this an auto property ?
                {
                    AddBackingFieldIfNeeded(accessor, node.AccessorList.Accessors.Any(acc => acc.IsKind(SyntaxKind.InitAccessorDeclaration)));

                    Context.EmitCilInstruction(ilSetVar, OpCodes.Ldarg_0);
                    if (!propertySymbol.IsStatic)
                        Context.EmitCilInstruction(ilSetVar, OpCodes.Ldarg_1);

                    var operand = MakeGenericTypeIfAppropriate(Context, propertySymbol, backingFieldVar, propertyDeclaringTypeVar);
                    Context.EmitCilInstruction(ilSetVar, propertySymbol.StoreOpCodeForFieldAccess(), operand);
                }
                else if (accessor.Body != null)
                {
                    StatementVisitor.Visit(Context, ilSetVar, accessor.Body);
                }
                else
                {
                    ExpressionVisitor.Visit(Context, ilSetVar, accessor.ExpressionBody);
                }

                Context.EmitCilInstruction(ilSetVar, OpCodes.Ret);
            }

            ScopedDefinitionVariable AddGetterMethodGuts(IPropertySymbol property, out string ilVar)
            {
                Context.WriteComment("Getter");

                var getMethodVar = Context.Naming.SyntheticVariable("get", ElementKind.Method);

                MethodDefinitionVariable methodDefinitionVariable;
                var exps = CecilDefinitionsFactory.Method(
                    Context,
                    property.ContainingType.ToDisplayString(),
                    getMethodVar,
                    property.GetMethod!.ToDisplayString(),
                    $"get_{propName}",
                    $"{accessorModifiers}",
                    parameters, 
                    [], // Properties cannot declare TypeParameters
                    ctx => propertyType,
                    out methodDefinitionVariable);
                
                Context.WriteCecilExpressions(exps);
                
                var scopedVariable = Context.DefinitionVariables.WithCurrentMethod(methodDefinitionVariable);
                
                AddToOverridenMethodsIfAppropriated(getMethodVar, property.GetMethod);
                if (property.HasCovariantGetter())
                    AddCecilExpression($"{getMethodVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(System.Runtime.CompilerServices.PreserveBaseOverridesAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");

                AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({getMethodVar});");

                AddCecilExpression($"{getMethodVar}.Body = new MethodBody({getMethodVar});");
                AddCecilExpression($"{propDefVar}.GetMethod = {getMethodVar};");

                if (propertySymbol.ContainingType.TypeKind != TypeKind.Interface)
                {
                    ilVar = Context.Naming.ILProcessor("get");
                    AddCecilExpression($"var {ilVar} = {getMethodVar}.Body.GetILProcessor();");
                }
                else
                {
                    ilVar = null;
                }

                return scopedVariable;
            }

            void AddExpressionBodiedGetterMethod(IPropertySymbol property)
            {
                using var _ = AddGetterMethodGuts(property, out var ilVar);
                ProcessExpressionBodiedGetter(ilVar, arrowExpression);
            }

            void AddGetterMethod(AccessorDeclarationSyntax accessor, IPropertySymbol propertySymbol)
            {
                using var _ = AddGetterMethodGuts(propertySymbol, out var ilVar);
                if (ilVar == null)
                    return;

                if (accessor.Body == null && accessor.ExpressionBody == null) //is this an auto property ?
                {
                    AddBackingFieldIfNeeded(accessor, node.AccessorList.Accessors.Any(acc => acc.IsKind(SyntaxKind.InitAccessorDeclaration)));

                    if (!propertySymbol.IsStatic)
                        Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
                    var operand = Utils.MakeGenericTypeIfAppropriate(Context, propertySymbol, backingFieldVar, propertyDeclaringTypeVar);
                    Context.EmitCilInstruction(ilVar, propertySymbol.LoadOpCodeForFieldAccess(), operand);

                    Context.EmitCilInstruction(ilVar, OpCodes.Ret);
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

            void ProcessExpressionBodiedGetter(string ilVar, ArrowExpressionClauseSyntax expression)
            {
                ExpressionVisitor.Visit(Context, ilVar, expression);
                Context.EmitCilInstruction(ilVar, OpCodes.Ret);
            }
        }

        private string AddPropertyDefinition(BasePropertyDeclarationSyntax propertyDeclarationSyntax, string propName, string propertyType)
        {
            var propDefVar = Context.Naming.PropertyDeclaration(propertyDeclarationSyntax);
            var exps = CecilDefinitionsFactory.PropertyDefinition(propDefVar, propName, propertyType);
            Context.WriteCecilExpressions(exps);
            
            return propDefVar;
        }
    }
}
