using System.Collections.Generic;
using System.Linq;
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
            var propertyType = ResolveType(node.Type);
            var propertyDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var propName = node.Identifier.ValueText;

            var propDefVar = AddPropertyDefinition(propName, propertyType);
            ProcessPropertyAccessors(node, propertyDeclaringTypeVar, propName, propertyType, propDefVar, NoParameters);

            AddCecilExpression($"{propertyDeclaringTypeVar}.Properties.Add({propDefVar});");
            
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetDoesNotMatch, SyntaxKind.FieldKeyword, propDefVar); // Normal property attrs
            HandleAttributesInMemberDeclaration(node.AttributeLists, TargetMatches,      SyntaxKind.FieldKeyword, backingFieldVar); // [field: attr], i.e, attr belongs to the backing field.
        }

        private void AddDefaultMemberAttribute(string definitionVar, string value)
        {
            var ctorVar = TempLocalVar("ctor");
            var customAttrVar = TempLocalVar("customAttr");
            var exps = new[]
            {
                $"var {ctorVar} = {ImportFromMainModule("typeof(System.Reflection.DefaultMemberAttribute).GetConstructor(new Type[] { typeof(string) })")};",
                $"var {customAttrVar} = new CustomAttribute({ctorVar});",
                $"{customAttrVar}.ConstructorArguments.Add(new CustomAttributeArgument({Context.TypeResolver.ResolvePredefinedType("String")}, \"{value}\"));",
                $"{definitionVar}.CustomAttributes.Add({customAttrVar});"
            };

            AddCecilExpressions(exps);
        }
        
        private void ProcessPropertyAccessors(BasePropertyDeclarationSyntax node, string propertyDeclaringTypeVar, string propName, string propertyType, string propDefVar, List<ParamData> parameters)
        {
            var propInfo = (IPropertySymbol) Context.SemanticModel.GetDeclaredSymbol(node);
            var declaringType = node.ResolveDeclaringType();
            foreach (var accessor in node.AccessorList.Accessors)
            {
                var accessorModifiers = node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName", propInfo.GetMethod ?? propInfo.SetMethod);
                switch (accessor.Keyword.Kind())
                {
                    case SyntaxKind.GetKeyword:
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
                        var ilProcessorExp = $"var {ilVar} = {getMethodVar}.Body.GetILProcessor();";
                        
                        AddCecilExpression(ilProcessorExp);
                        if (accessor.Body == null) //is this an auto property ?
                        {
                            AddBackingFieldIfNeeded(accessor);

                            AddCilInstruction(ilVar, OpCodes.Ldarg_0); // TODO: This assumes instance properties...
                            AddCilInstruction(ilVar, OpCodes.Ldfld, MakeGenericTypeIfAppropriate(propInfo, backingFieldVar, propertyDeclaringTypeVar));
                            
                            AddCilInstruction(ilVar, OpCodes.Ret);
                        }
                        else
                        {
                            StatementVisitor.Visit(Context, ilVar, accessor.Body);
                        }

                        break;

                    case SyntaxKind.SetKeyword:
                        var setMethodVar = TempLocalVar(propertyDeclaringTypeVar + "_set_");
                        var localParams = new List<string>(parameters.Select(p => p.Type));
                        localParams.Add(Context.GetTypeInfo(node.Type).Type.Name); // Setters always have at least one `value` parameter.
                        Context.DefinitionVariables.RegisterMethod(declaringType.Identifier.Text, $"set_{propName}", localParams.ToArray(), setMethodVar);
                        var ilSetVar = TempLocalVar("ilVar_set_");

                        AddCecilExpression($"var {setMethodVar} = new MethodDefinition(\"set_{propName}\", {accessorModifiers}, {Context.TypeResolver.ResolvePredefinedType("Void")});");
                        parameters.ForEach(p => AddCecilExpression($"{setMethodVar}.Parameters.Add({p.VariableName});"));
                        AddCecilExpression($"{propertyDeclaringTypeVar}.Methods.Add({setMethodVar});");

                        AddCecilExpression($"{setMethodVar}.Body = new MethodBody({setMethodVar});");
                        AddCecilExpression($"{propDefVar}.SetMethod = {setMethodVar};");

                        AddCecilExpression($"{setMethodVar}.Parameters.Add(new ParameterDefinition({propertyType}));");
                        AddCecilExpression($"var {ilSetVar} = {setMethodVar}.Body.GetILProcessor();");

                        if (propInfo.ContainingType.TypeKind == TypeKind.Interface)
                            break;

                        if (accessor.Body == null) //is this an auto property ?
                        {
                            AddBackingFieldIfNeeded(accessor);

                            AddCilInstruction(ilSetVar, OpCodes.Ldarg_0); // TODO: This assumes instance properties...
                            AddCilInstruction(ilSetVar, OpCodes.Ldarg_1);
                            AddCilInstruction(ilSetVar, OpCodes.Stfld, MakeGenericTypeIfAppropriate(propInfo, backingFieldVar, propertyDeclaringTypeVar));
                        }
                        else
                        {
                            StatementVisitor.Visit(Context, ilSetVar, accessor.Body);
                        }

                        AddCilInstruction(ilSetVar, OpCodes.Ret);
                        break;
                }
            }

            void AddBackingFieldIfNeeded(AccessorDeclarationSyntax accessor)
            {
                if (backingFieldVar != null)
                {
                    return;
                }

                backingFieldVar = TempLocalVar("bf");
                var backingFieldName = $"<{propName}>k__BackingField";
                var backingFieldExps = new[]
                {
                    $"var {backingFieldVar} = new FieldDefinition(\"{backingFieldName}\", {ModifiersToCecil("FieldAttributes", accessor.Modifiers, "Private")}, {propertyType});",
                    $"{propertyDeclaringTypeVar}.Fields.Add({backingFieldVar});"
                };

                AddCecilExpressions(backingFieldExps);
            }
        }

        private string MakeGenericTypeIfAppropriate(IPropertySymbol propInfo, string existingFieldVar, string propertyDeclaringTypeVar)
        {
            if (!(propInfo.ContainingSymbol is INamedTypeSymbol ts) || !ts.IsGenericType || !propInfo.IsDefinedInCurrentType(Context))
                return existingFieldVar;

            //TODO: Register the following variable?
            var genTypeVar = TempLocalVar($"gt_{propInfo.Name}_{propInfo.Parameters.Length}");
            AddCecilExpressions(new[]
            {
                $"var {genTypeVar} = {propertyDeclaringTypeVar}.MakeGenericInstanceType({propertyDeclaringTypeVar}.GenericParameters.ToArray());",
                $"var {genTypeVar}_ = new FieldReference({backingFieldVar}.Name, {backingFieldVar}.FieldType, {genTypeVar});",
            });

            return $"{genTypeVar}_";
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
