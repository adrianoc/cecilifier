using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
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
            Context.WriteNewLine();
            Context.WriteComment($"** Property indexer **");
            
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var propName = "Item";

            AddDefaultMemberAttribute(propertyDeclaringTypeVar, propName);
            var propDefVar = AddPropertyDefinition(propName, propertyType);

            var paramsVar = new List<ParamData>();
            foreach (var parameter in node.ParameterList.Parameters)
            {
                var paramVar = TempLocalVar(parameter.Identifier.ValueText);
                paramsVar.Add(new ParamData
                {
                    VariableName = paramVar,
                    Type = Context.GetTypeInfo(parameter.Type).Type.Name
                });

                var exps = CecilDefinitionsFactory.Parameter(parameter, Context.SemanticModel, propDefVar, paramVar, ResolveType(parameter.Type));
                AddCecilExpressions(exps);
            }

            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, paramsVar);

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

            var propDefVar = AddPropertyDefinition(propName, propertyType);
            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, NoParameters);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");
            
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches,      SyntaxKind.FieldKeyword, backingFieldVar); // [field: attr], i.e, attr belongs to the backing field.
        }

        private bool PropertyAlreadyProcessed(PropertyDeclarationSyntax node)
        {
            var propInfo = (IPropertySymbol) Context.SemanticModel.GetDeclaredSymbol(node);
            if (propInfo == null)
                return false;
            
            // check the methods of the property because we do not register the property itself, only its methods. 
            var methodToCheck = propInfo?.GetMethod?.Name ?? propInfo?.SetMethod?.Name;
            var d = Context.DefinitionVariables.GetVariable(methodToCheck, MemberKind.Method, propInfo.ContainingType.Name);
            return d.IsValid;
        }

        private void AddDefaultMemberAttribute(string definitionVar, string value)
        {
            var ctorVar = TempLocalVar("ctor");
            var customAttrVar = TempLocalVar("customAttr");
            var exps = new[]
            {
                $"var {ctorVar} = {ImportFromMainModule("typeof(System.Reflection.DefaultMemberAttribute).GetConstructor(new Type[] { typeof(string) })")};",
                $"var {customAttrVar} = new CustomAttribute({ctorVar});",
                $"{customAttrVar}.ConstructorArguments.Add(new CustomAttributeArgument({Context.TypeResolver.Resolve(GetSpecialType(SpecialType.System_String))}, \"{value}\"));",
                $"{definitionVar}.CustomAttributes.Add({customAttrVar});"
            };

            AddCecilExpressions(exps);
        }
        
        private void ProcessPropertyAccessors(BasePropertyDeclarationSyntax node, string propertyDeclaringTypeVar, string propName, string propertyType, string propDefVar, List<ParamData> parameters)
        {
            var propInfo = (IPropertySymbol) Context.SemanticModel.GetDeclaredSymbol(node);
            var declaringType = node.ResolveDeclaringType<TypeDeclarationSyntax>();
            foreach (var accessor in node.AccessorList!.Accessors)
            {
                Context.WriteNewLine();
                var accessorModifiers = node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName", propInfo.GetMethod ?? propInfo.SetMethod);
                switch (accessor.Keyword.Kind())
                {
                    case SyntaxKind.GetKeyword:
                        Context.WriteComment(" Getter");
                        var getMethodVar = TempLocalVar(propertyDeclaringTypeVar + "_get_");
                        Context.DefinitionVariables.RegisterMethod(declaringType.Identifier.Text, $"get_{propName}", parameters.Select(p => p.Type).ToArray(), getMethodVar);

                        AddCecilExpression($"var {getMethodVar} = new MethodDefinition(\"get_{propName}\", {accessorModifiers}, {propertyType});");
                        parameters.ForEach(p => AddCecilExpression($"{getMethodVar}.Parameters.Add({p.VariableName});"));
                        AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({getMethodVar});");

                        AddCecilExpression($"{getMethodVar}.Body = new MethodBody({getMethodVar});");
                        AddCecilExpression($"{propDefVar}.GetMethod = {getMethodVar};");

                        if (propInfo.ContainingType.TypeKind == TypeKind.Interface)
                            break;
                        
                        var ilVar = TempLocalVar("ilVar_get_");
                        AddCecilExpression($"var {ilVar} = {getMethodVar}.Body.GetILProcessor();");
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
                            var expressionVisitor = new ExpressionVisitor(Context, ilVar);
                            accessor.ExpressionBody.Accept(expressionVisitor);
                            AddCilInstruction(ilVar, OpCodes.Ret);
                        }
                        break;

                    case SyntaxKind.InitKeyword:
                        Context.WriteComment(" Init");
                        var setterReturnType = $"new RequiredModifierType({ImportExpressionForType(typeof(IsExternalInit))}, {Context.TypeResolver.Resolve(GetSpecialType(SpecialType.System_Void))})";
                        AddSetterMethod(accessorModifiers, setterReturnType, accessor);
                        break;
                    
                    case SyntaxKind.SetKeyword:
                        Context.WriteComment(" Setter");
                        AddSetterMethod(accessorModifiers, Context.TypeResolver.Resolve(GetSpecialType(SpecialType.System_Void)), accessor);
                        break; 
                    default:
                        throw new NotImplementedException($"Accessor: {accessor.Keyword}");
                }
            }

            void AddBackingFieldIfNeeded(AccessorDeclarationSyntax accessor, bool hasInitProperty)
            {
                if (backingFieldVar != null)
                    return;

                backingFieldVar = TempLocalVar("bf");
                var backingFieldName = $"<{propName}>k__BackingField";
                var modifiers = ModifiersToCecil("FieldAttributes", accessor.Modifiers, "Private");
                if (hasInitProperty)
                {
                    modifiers = modifiers + " | FieldAttributes.InitOnly";
                }
                
                var backingFieldExps = new[]
                {
                    $"var {backingFieldVar} = new FieldDefinition(\"{backingFieldName}\", {modifiers}, {propertyType});",
                    $"{propertyDeclaringTypeVar}.Fields.Add({backingFieldVar});"
                };

                AddCecilExpressions(backingFieldExps);
            }

            void AddSetterMethod(string accessorModifiers, string setterReturnType, AccessorDeclarationSyntax accessor)
            {
                var setMethodVar = TempLocalVar(propertyDeclaringTypeVar + "_set_");
                var localParams = new List<string>(parameters.Select(p => p.Type));
                localParams.Add(Context.GetTypeInfo(node.Type).Type.Name); // Setters always have at least one `value` parameter.
                Context.DefinitionVariables.RegisterMethod(declaringType.Identifier.Text, $"set_{propName}", localParams.ToArray(), setMethodVar);
                var ilSetVar = TempLocalVar("ilVar_set_");

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
                    var expressionVisitor = new ExpressionVisitor(Context, ilSetVar);
                    accessor.ExpressionBody.Accept(expressionVisitor);
                }

                AddCilInstruction(ilSetVar, OpCodes.Ret);
            }
        }

        private string AddPropertyDefinition(string propName, string propertyType)
        {
            var propDefVar = TempLocalVar($"{propName}DefVar");
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
