using System.Collections.Generic;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class PropertyDeclarationVisitor : SyntaxWalkerBase
    {
        public PropertyDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var propName = "Item";
            
            AddDefaultMemberAttribute(propertyDeclaringTypeVar, propName);
            var propDefVar = AddPropertyDefinition(propName, propertyType);

            var paramsVar = new List<string>();
            foreach (var parameter in node.ParameterList.Parameters)
            {
                var paramVar = TempLocalVar(parameter.Identifier.ValueText);
                paramsVar.Add(paramVar);
                
                var exps = CecilDefinitionsFactory.Parameter(parameter, Context.SemanticModel, propDefVar, paramVar, ResolveType(parameter.Type));
                AddCecilExpressions(exps);
            }

            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, paramsVar);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");
        }

        private void AddDefaultMemberAttribute(string definitionVar, string value)
        {
            var ctorVar = TempLocalVar("ctor");
            var customAttrVar  = TempLocalVar("customAttr");
            var exps = new[]
            {
                $"var {ctorVar} = assembly.MainModule.ImportReference(typeof(System.Reflection.DefaultMemberAttribute).GetConstructor(new Type[] {{ typeof(string) }}));",
                $"var {customAttrVar} = new CustomAttribute({ctorVar});",
                $"{customAttrVar}.ConstructorArguments.Add(new CustomAttributeArgument({ResolvePredefinedType("String")}, \"{value}\"));",
                $"{definitionVar}.CustomAttributes.Add({customAttrVar});"
            };
            
            AddCecilExpressions(exps);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var propName = node.Identifier.ValueText;
            
            var propDefVar = AddPropertyDefinition(propName, propertyType);

            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");
        }

        private void ProcessPropertyAccessors(BasePropertyDeclarationSyntax node, string propertyDeclaringTypeVar, string propName, string propertyType, string propDefVar, List<string> parameters = null)
        {
            foreach (var accessor in node.AccessorList.Accessors)
            {
                var accessorModifiers = node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName");
                switch (accessor.Keyword.Kind())
                {
                    case SyntaxKind.GetKeyword:
                        var getMethodVar = TempLocalVar(propertyDeclaringTypeVar + "_get_");
                        Context.DefinitionVariables.Register(string.Empty, $"get_{propName}", MemberKind.Method, getMethodVar);

                        AddCecilExpression($"var {getMethodVar} = new MethodDefinition(\"get_{propName}\", {accessorModifiers}, {propertyType});");
                        parameters?.ForEach(paramVar => AddCecilExpression($"{getMethodVar}.Parameters.Add({paramVar});"));
                        AddCecilExpression($"{getMethodVar}.Body = new MethodBody({getMethodVar});");
                        AddCecilExpression($"{propDefVar}.GetMethod = {getMethodVar};");
                        AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({getMethodVar});");

                        var ilVar = TempLocalVar("ilVar_get_");
                        var ilProcessorExp = $"var {ilVar} = {getMethodVar}.Body.GetILProcessor();";
                        AddCecilExpression(ilProcessorExp);

                        if (accessor.Body == null) //is this an auto property ?
                        {
                            AddBackingFieldIfNeeded(accessor);

                            AddCilInstruction(ilVar, OpCodes.Ldarg_0); // TODO: This assumes instance properties...
                            AddCilInstruction(ilVar, OpCodes.Ldfld, backingFieldVar);
                            AddCilInstruction(ilVar, OpCodes.Ret);
                        }
                        else
                        {
                            StatementVisitor.Visit(Context, ilVar, accessor.Body);
                        }

                        break;

                    case SyntaxKind.SetKeyword:
                        var setMethodVar = TempLocalVar(propertyDeclaringTypeVar + "_set_");
                        Context.DefinitionVariables.Register(string.Empty, $"set_{propName}", MemberKind.Method, setMethodVar);
                        var ilSetVar = TempLocalVar("ilVar_set_");

                        AddCecilExpression($"var {setMethodVar} = new MethodDefinition(\"set_{propName}\", {accessorModifiers}, {ResolvePredefinedType("Void")});");
                        parameters?.ForEach(paramVar => AddCecilExpression($"{setMethodVar}.Parameters.Add({paramVar});"));
                        
                        AddCecilExpression($"{setMethodVar}.Body = new MethodBody({setMethodVar});");
                        AddCecilExpression($"{propDefVar}.SetMethod = {setMethodVar};");

                        AddCecilExpression($"{setMethodVar}.Parameters.Add(new ParameterDefinition({propertyType}));");
                        AddCecilExpression($"var {ilSetVar} = {setMethodVar}.Body.GetILProcessor();");

                        if (accessor.Body == null) //is this an auto property ?
                        {
                            AddBackingFieldIfNeeded(accessor);

                            AddCilInstruction(ilSetVar, OpCodes.Ldarg_0); // TODO: This assumes instance properties...
                            AddCilInstruction(ilSetVar, OpCodes.Ldarg_1);
                            AddCilInstruction(ilSetVar, OpCodes.Stfld, backingFieldVar);
                        }
                        else
                        {
                            StatementVisitor.Visit(Context, ilSetVar, accessor.Body);
                        }

                        AddCilInstruction(ilSetVar, OpCodes.Ret);
                        AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({setMethodVar});");
                        break;
                }
            }
            
            void AddBackingFieldIfNeeded(AccessorDeclarationSyntax accessor)
            {
                if (backingFieldVar != null)
                    return;

                backingFieldVar = TempLocalVar("bf");
                var backingFieldName = $"<{propName}>k__BackingField";
                var x = new[]
                {
                    $"var {backingFieldVar} = new FieldDefinition(\"{backingFieldName}\", {ModifiersToCecil("FieldAttributes", accessor.Modifiers, "Private")}, {propertyType});",
                    $"{propertyDeclaringTypeVar}.Fields.Add({backingFieldVar});"
                };

                AddCecilExpressions(x);
            }

        }

        private string AddPropertyDefinition(string propName, string propertyType)
        {
            var propDefVar = TempLocalVar($"{propName}DefVar");
            var propDefExp = $"var {propDefVar} = new PropertyDefinition(\"{propName}\", PropertyAttributes.None, {propertyType});";
            AddCecilExpression(propDefExp);

            return propDefVar;
        }

        private string backingFieldVar;
    }
}