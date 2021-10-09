using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.AST
{
    internal class PropertyDeclarationVisitor : SyntaxWalkerBase
    {
        private static readonly List<ParamData> NoParameters = new List<ParamData>();
        private string backingFieldVar;

        public PropertyDeclarationVisitor(IVisitorContext context) : base(context) { }

        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            if (PropertyAlreadyProcessed(node))
                return;
            
            Context.WriteNewLine();
            Context.WriteComment($"** Property indexer **");
            
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var propName = "Item";

            AddDefaultMemberAttribute(propertyDeclaringTypeVar, propName);
            var propDefVar = AddPropertyDefinition(node, propName, propertyType);

            var paramsVar = new List<ParamData>();
            foreach (var parameter in node.ParameterList.Parameters)
            {
                var paramVar = Context.Naming.Parameter(parameter);
                paramsVar.Add(new ParamData
                {
                    VariableName = paramVar,
                    Type = Context.GetTypeInfo(parameter.Type).Type.Name
                });

                var exps = CecilDefinitionsFactory.Parameter(parameter, Context.SemanticModel, propDefVar, paramVar, ResolveType(parameter.Type));
                AddCecilExpressions(exps);
                Context.DefinitionVariables.RegisterNonMethod(string.Empty, parameter.Identifier.ValueText, MemberKind.Parameter, paramVar);
            }

            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, paramsVar, node.ExpressionBody);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");
            
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches,      SyntaxKind.FieldKeyword, backingFieldVar); // [field: attr], i.e, attr belongs to the backing field.
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (PropertyAlreadyProcessed(node))
                return;
            
            Context.WriteNewLine();
            Context.WriteComment($"** Property: {node.Identifier} **");
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var propName = node.Identifier.ValueText;

            var propDefVar = AddPropertyDefinition(node, propName, propertyType);
            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, NoParameters, node.ExpressionBody);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");
            
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches,      SyntaxKind.FieldKeyword, backingFieldVar); // [field: attr], i.e, attr belongs to the backing field.
        }

        private bool PropertyAlreadyProcessed(BasePropertyDeclarationSyntax node)
        {
            var propInfo = (IPropertySymbol) Context.SemanticModel.GetDeclaredSymbol(node);
            if (propInfo == null)
                return false;
            
            // check the methods of the property because we do not register the property itself, only its methods. 
            var methodToCheck = propInfo?.GetMethod ?? propInfo?.SetMethod;
            var found = Context.DefinitionVariables.GetMethodVariable(methodToCheck.AsMethodDefinitionVariable());
            return found.IsValid;
        }

        private void AddDefaultMemberAttribute(string definitionVar, string value)
        {
            var ctorVar = Context.Naming.MemberReference("ctor", Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName);
            var customAttrVar = Context.Naming.CustomAttribute("DefaultMember"); 
            
            var exps = new[]
            {
                $"var {ctorVar} = {ImportFromMainModule("typeof(System.Reflection.DefaultMemberAttribute).GetConstructor(new Type[] { typeof(string) })")};",
                $"var {customAttrVar} = new CustomAttribute({ctorVar});",
                $"{customAttrVar}.ConstructorArguments.Add(new CustomAttributeArgument({Context.TypeResolver.Bcl.System.String}, \"{value}\"));",
                $"{definitionVar}.CustomAttributes.Add({customAttrVar});"
            };

            AddCecilExpressions(exps);
        }
        
        private void ProcessPropertyAccessors(BasePropertyDeclarationSyntax node, string propertyDeclaringTypeVar, string propName, string propertyType, string propDefVar, List<ParamData> parameters, ArrowExpressionClauseSyntax? arrowExpression)
        {
            var propInfo = (IPropertySymbol) Context.SemanticModel.GetDeclaredSymbol(node);
            var accessorModifiers = node.Modifiers.MethodModifiersToCecil((targetEnum, modifiers, defaultAccessibility) => ModifiersToCecil(modifiers, targetEnum, defaultAccessibility), "MethodAttributes.SpecialName", propInfo.GetMethod ?? propInfo.SetMethod);

            var declaringType = node.ResolveDeclaringType<TypeDeclarationSyntax>();
            if (arrowExpression != null)
            {
                AddExpressionBodiedGetterMethod();
                return;
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
                        Context.WriteComment(" Init");
                        var setterReturnType = $"new RequiredModifierType({ImportExpressionForType(typeof(IsExternalInit))}, {Context.TypeResolver.Bcl.System.Void})";
                        AddSetterMethod(setterReturnType, accessor);
                        break;
                    
                    case SyntaxKind.SetKeyword:
                        Context.WriteComment(" Setter");
                        AddSetterMethod(Context.TypeResolver.Bcl.System.Void, accessor);
                        break; 
                    default:
                        throw new NotImplementedException($"Accessor: {accessor.Keyword}");
                }
            }

            void AddBackingFieldIfNeeded(AccessorDeclarationSyntax accessor, bool hasInitProperty)
            {
                if (backingFieldVar != null)
                    return;

                backingFieldVar = Context.Naming.FieldDeclaration(node, "bf");
                var backingFieldName = $"<{propName}>k__BackingField";
                var modifiers = ModifiersToCecil(accessor.Modifiers, "FieldAttributes", "Private");
                if (hasInitProperty)
                {
                    modifiers = modifiers + " | FieldAttributes.InitOnly";
                }
                
                var backingFieldExps = new[]
                {
                    //TODO: NOW: CecilDefinitionsFactory.Field()
                    $"var {backingFieldVar} = new FieldDefinition(\"{backingFieldName}\", {modifiers}, {propertyType});",
                    $"{propertyDeclaringTypeVar}.Fields.Add({backingFieldVar});"
                };

                AddCecilExpressions(backingFieldExps);
            }

            void AddSetterMethod(string setterReturnType, AccessorDeclarationSyntax accessor)
            {
                var setMethodVar = Context.Naming.SyntheticVariable("set", ElementKind.LocalVariable);
                
                var localParams = new List<string>(parameters.Select(p => p.Type));
                localParams.Add(Context.GetTypeInfo(node.Type).Type.Name); // Setters always have at least one `value` parameter.
                Context.DefinitionVariables.RegisterMethod(declaringType.Identifier.Text, $"set_{propName}", localParams.ToArray(), setMethodVar);
                var ilSetVar = Context.Naming.ILProcessor("set", declaringType.Identifier.Text);

                //TODO : NEXT : try to use CecilDefinitionsFactory.Method()
                AddCecilExpression($"var {setMethodVar} = new MethodDefinition(\"set_{propName}\", {accessorModifiers}, {setterReturnType});");
                parameters.ForEach(p => AddCecilExpression($"{setMethodVar}.Parameters.Add({p.VariableName});"));
                AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({setMethodVar});");

                AddCecilExpression($"{setMethodVar}.Body = new MethodBody({setMethodVar});");
                AddCecilExpression($"{propDefVar}.SetMethod = {setMethodVar};");

                AddCecilExpression($"{setMethodVar}.Parameters.Add(new ParameterDefinition(\"value\", ParameterAttributes.None, {propertyType}));");
                AddCecilExpression($"var {ilSetVar} = {setMethodVar}.Body.GetILProcessor();");

                if (propInfo.ContainingType.TypeKind == TypeKind.Interface)
                    return;

                if (accessor.Body == null && accessor.ExpressionBody == null) //is this an auto property ?
                {
                    AddBackingFieldIfNeeded(accessor, node.AccessorList.Accessors.Any(acc => acc.IsKind(SyntaxKind.InitAccessorDeclaration)));

                    AddCilInstruction(ilSetVar, OpCodes.Ldarg_0); // TODO: This assumes instance properties...
                    AddCilInstruction(ilSetVar, OpCodes.Ldarg_1);
                    AddCilInstruction(ilSetVar, OpCodes.Stfld, Utils.MakeGenericTypeIfAppropriate(Context, propInfo, backingFieldVar, propertyDeclaringTypeVar));
                }
                else if (accessor.Body != null)
                {
                    StatementVisitor.Visit(Context, ilSetVar, accessor.Body);
                }
                else
                {
                    ExpressionVisitor.Visit(Context, ilSetVar, accessor.ExpressionBody);
                }

                AddCilInstruction(ilSetVar, OpCodes.Ret);
            }

            string AddGetterMethodGuts(CSharpSyntaxNode accessor)
            {
                Context.WriteComment(" Getter");
                var getMethodVar = Context.Naming.SyntheticVariable("get", ElementKind.Method);
                Context.DefinitionVariables.RegisterMethod(declaringType.Identifier.Text, $"get_{propName}", parameters.Select(p => p.Type).ToArray(), getMethodVar);

                AddCecilExpression($"var {getMethodVar} = new MethodDefinition(\"get_{propName}\", {accessorModifiers}, {propertyType});");
                parameters.ForEach(p => AddCecilExpression($"{getMethodVar}.Parameters.Add({p.VariableName});"));
                AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({getMethodVar});");

                AddCecilExpression($"{getMethodVar}.Body = new MethodBody({getMethodVar});");
                AddCecilExpression($"{propDefVar}.GetMethod = {getMethodVar};");

                if (propInfo.ContainingType.TypeKind == TypeKind.Interface)
                    return null;

                var ilVar = Context.Naming.ILProcessor("get", declaringType.Identifier.Text);
                AddCecilExpression($"var {ilVar} = {getMethodVar}.Body.GetILProcessor();");
                return ilVar;
            }
            
            void AddExpressionBodiedGetterMethod()
            {
                var ilVar = AddGetterMethodGuts(arrowExpression);
                ProcessExpressionBodiedGetter(ilVar, arrowExpression);
            }
            
            void AddGetterMethod(AccessorDeclarationSyntax accessor)
            {
                var ilVar = AddGetterMethodGuts(accessor);
                if (ilVar == null)
                    return;
                
                if (accessor.Body == null && accessor.ExpressionBody == null) //is this an auto property ?
                {
                    AddBackingFieldIfNeeded(accessor, node.AccessorList.Accessors.Any(acc => acc.IsKind(SyntaxKind.InitAccessorDeclaration)));

                    AddCilInstruction(ilVar, OpCodes.Ldarg_0); // TODO: This assumes instance properties...
                    AddCilInstruction(ilVar, OpCodes.Ldfld, Utils.MakeGenericTypeIfAppropriate(Context, propInfo, backingFieldVar, propertyDeclaringTypeVar));

                    AddCilInstruction(ilVar, OpCodes.Ret);
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

            void ProcessExpressionBodiedGetter(string ilVar, ArrowExpressionClauseSyntax arrowExpression)
            {
                ExpressionVisitor.Visit(Context, ilVar, arrowExpression);
                AddCilInstruction(ilVar, OpCodes.Ret);
            }
        }

        private string AddPropertyDefinition(BasePropertyDeclarationSyntax propertyDeclarationSyntax, string propName, string propertyType)
        {
            var propDefVar = Context.Naming.PropertyDeclaration(propertyDeclarationSyntax);
            var propDefExp = $"var {propDefVar} = new PropertyDefinition(\"{propName}\", PropertyAttributes.None, {propertyType});";
            AddCecilExpression(propDefExp);

            return propDefVar;
        }
    }

    struct ParamData
    {
        public string VariableName; // the name of the variable in the generated code that holds the ParameterDefinition instance.
        public string Type;
    }
}
