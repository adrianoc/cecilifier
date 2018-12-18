using Cecilifier.Core.Extensions;
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

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = ResolveTypeLocalVariable(Context.CurrentType);
            var propName = node.Identifier.ValueText;
            
            var propDefVar = AddPropertyDefinition(propName, propertyType);

            foreach (var accessor in node.AccessorList.Accessors)
            {
                var accessorModifiers = node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName");
                switch (accessor.Keyword.Kind())
                {
                    case SyntaxKind.GetKeyword:
                        var getMethodVar = TempLocalVar(propertyDeclaringTypeVar + "_get_");
                        
                        AddCecilExpression($"var {getMethodVar} = new MethodDefinition(\"get_{propName}\", {accessorModifiers}, {propertyType});");
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
                        var ilSetVar = TempLocalVar("ilVar_set_");
                        
                        AddCecilExpression($"var {setMethodVar} = new MethodDefinition(\"set_{propName}\", {accessorModifiers}, {ResolvePredefinedType("Void")});");

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

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");

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
            var propDefVar = $"{propName}DefVar";
            var propDefExp = $"var {propDefVar} = new PropertyDefinition(\"{propName}\", PropertyAttributes.None, {propertyType});";
            AddCecilExpression(propDefExp);

            return propDefVar;
        }

        private string backingFieldVar;
    }
}