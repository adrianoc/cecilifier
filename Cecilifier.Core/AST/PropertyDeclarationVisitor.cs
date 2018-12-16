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
                var accessorModifiers = accessor.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName");
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
                        
                        StatementVisitor.Visit(Context, ilVar, accessor.Body);
                        break;
                    
                    case SyntaxKind.SetKeyword:
                        var setMethodVar = TempLocalVar(propertyDeclaringTypeVar + "_set_");
                        var ilSetVar = TempLocalVar("ilVar_set_");
                        
                        AddCecilExpression($"var {setMethodVar} = new MethodDefinition(\"set_{propName}\", {accessorModifiers}, {ResolvePredefinedType("Void")});");

                        AddCecilExpression($"{setMethodVar}.Body = new MethodBody({setMethodVar});");
                        AddCecilExpression($"{propDefVar}.SetMethod = {setMethodVar};");
                        
                        AddCecilExpression($"{setMethodVar}.Parameters.Add(new ParameterDefinition({propertyType}));");
                        AddCecilExpression($"var {ilSetVar} = {setMethodVar}.Body.GetILProcessor();");
                        StatementVisitor.Visit(Context, ilSetVar, accessor.Body);
                        AddCilInstruction(ilSetVar, OpCodes.Ret);
                        AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({setMethodVar});");
                        break;
                }
            }

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");
        }

        private string AddPropertyDefinition(string propName, string propertyType)
        {
            var propDefVar = $"{propName}DefVar";
            var propDefExp = $"var {propDefVar} = new PropertyDefinition(\"{propName}\", PropertyAttributes.None, {propertyType});";
            AddCecilExpression(propDefExp);

            return propDefVar;
        }
    }
}