using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Misc
{
    internal sealed class CecilDefinitionsFactory
    {
        public static string Constructor(out string ctorLocalVar, string typeName, string methodAccessibility, string methodDefinitionPropertyValues = null)
        {
            ctorLocalVar = MethodExtensions.LocalVariableNameFor(typeName, "ctor", "");

            var exp = $@"var {ctorLocalVar} = new MethodDefinition("".ctor"", {methodAccessibility} | MethodAttributes.HideBySig | {ConstructorDeclarationVisitor.CtorFlags}, assembly.MainModule.TypeSystem.Void)";
            if (methodDefinitionPropertyValues != null)
                exp = exp + $"{{ {methodDefinitionPropertyValues} }}";

            return exp + ";";
        }

        public static IEnumerable<string> Type(IVisitorContext context, string typeVar, string typeName, string attrs, string baseTypeName, bool isStructWithNoFields, IEnumerable<string> interfaces, params string[] properties)
        {
            var exps = new List<string>();
            var typeDefExp = $"var {typeVar} = new TypeDefinition(\"{context.Namespace}\", \"{typeName}\", {attrs}{ (!string.IsNullOrWhiteSpace(baseTypeName) ? ", " + baseTypeName : "")})";
            if (properties.Length > 0)
            {
                exps.Add($"{typeDefExp} {{ {string.Join(',', properties) } }};");
            }
            else
            {
                exps.Add($"{typeDefExp};");    
            }
        
            foreach (var itfName in interfaces)
            {
                exps.Add($"{typeVar}.Interfaces.Add(new InterfaceImplementation({itfName}));");
            }

            var currentType = context.DefinitionVariables.GetLastOf(MemberKind.Type);
            if (currentType.IsValid)
            {
                // type is a inner type of *context.CurrentType* 
                exps.Add($"{currentType.VariableName}.NestedTypes.Add({typeVar});");
            }
            else
            {
                exps.Add($"assembly.MainModule.Types.Add({typeVar});");
            }

            if (isStructWithNoFields)
            {
                exps.Add($"{typeVar}.ClassSize = 1;");	
                exps.Add($"{typeVar}.PackingSize = 0;");	
            }
            return exps;
        }
        
        public static IEnumerable<string> Field(string declaringTypeVar, string fieldVar, string name, string fieldType, string fieldAttributes, params string[] properties)
        {
            var exps = new List<string>();
            var fieldExp = ($"var {fieldVar} = new FieldDefinition(\"{name}\", {fieldAttributes}, {fieldType})");
            if (properties.Length > 0)
                exps.Add($"{fieldExp} {{ {string.Join(',', properties) } }};");
            else
                exps.Add($"{fieldExp};");
            
            exps.Add($"{declaringTypeVar}.Fields.Add({fieldVar});");
            return exps;
        }
        
        public static IEnumerable<string> Parameter(ParameterSyntax node, SemanticModel semanticModel, string methodVar, string paramVar, string resolvedType)
        {
            var exps = new List<string>();
            var paramSymbol = semanticModel.GetDeclaredSymbol(node);
            if (paramSymbol.RefKind != RefKind.None)
            {
                resolvedType = "new ByReferenceType(" + resolvedType + ")";
            }

            exps.Add($"var {paramVar} = new ParameterDefinition(\"{node.Identifier.Text}\", ParameterAttributes.None, {resolvedType});");

            AddExtraAttributes(exps, paramVar, paramSymbol);
			
            if (node.GetFirstToken().Kind() == SyntaxKind.ParamsKeyword)
            {
                exps.Add($"{paramVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(ParamArrayAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
            }

            exps.Add($"{methodVar}.Parameters.Add({paramVar});");
            
            return exps;
        }
        
        /*
 * cecil expressions for:
 * - Find out the ctor to be used
 * -
 *
 *  node.AttributeLists[0].Attributes[0].ArgumentList.Arguments[0]
 * 
 *			                                           attr.Name							 attr.ArgumentList.Arguments { Expression }  (NameEquals == null ??)
 *                                                 ----------------                          ---------------------------       
 * 
 *  var ctor = a.MainModule.ImportReference(typeof(ObsoleteAttribute).GetConstructor(new[] { typeof(string), typeof(bool) }));
 *  var customAttribute = new CustomAttribute(ctor);
 *
 *    																	Symbol(attr.argument.Expression)
 *                                                                       ==============================
 *  customAttribute.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.String, "EU sou a lenda"));
 *  customAttribute.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.Boolean, true));
 *  customAttribute.Properties.Add(new CustomAttributeNamedArgument("IsError", new CustomAttributeArgument(a.MainModule.TypeSystem.Boolean, true)));
 *  t.CustomAttributes.Add(customAttribute);
 * 
 */
        public static IEnumerable<string> Attribute(string typeVar, IVisitorContext context, AttributeSyntax attribute, Func<ITypeSymbol, AttributeArgumentSyntax[], string> ctorResolver)
        {
            var exps = new List<string>();
            var attrType = context.GetTypeInfo(attribute.Name);

            var customAttrVar = $"ca_{context.NextLocalVariableTypeId()}";

            var attributeArguments  = attribute.ArgumentList == null 
                                        ? new AttributeArgumentSyntax[0]
                                        : attribute.ArgumentList.Arguments.Where(arg => arg.NameEquals == null).ToArray();

            var ctorExp = ctorResolver(attrType.Type, attributeArguments);
            exps.Add($"var {customAttrVar} = new CustomAttribute({ctorExp});");
            
            if (attribute.ArgumentList != null)
            {
                foreach (var attrArg in attributeArguments)
                {
                    var argType = context.GetTypeInfo(attrArg.Expression);
                    exps.Add($"{customAttrVar}.ConstructorArguments.Add({CustomAttributeArgument(argType, attrArg)});");
                }

                // process properties
                foreach (var propertyArg in attribute.ArgumentList.Arguments.Except(attributeArguments))
                {
                    var argType = context.GetTypeInfo(propertyArg.Expression);
                    exps.Add($"{customAttrVar}.Properties.Add(new CustomAttributeNamedArgument(\"{propertyArg.NameEquals.Name.Identifier.ValueText}\", {CustomAttributeArgument(argType, propertyArg)}));");                    
                }
            }
            exps.Add($"{typeVar}.CustomAttributes.Add({customAttrVar});");
            
            return exps;

            string CustomAttributeArgument(Microsoft.CodeAnalysis.TypeInfo argType, AttributeArgumentSyntax attrArg)
            {
                return $"new CustomAttributeArgument(assembly.MainModule.ImportReference(typeof({argType.Type.FullyQualifiedName()})), {attrArg.Expression.EvaluateConstantExpression(context.SemanticModel)})";
            }
        }
        
        private static void AddExtraAttributes(IList<string> exps, string paramVar, IParameterSymbol symbol)
        {
            if (symbol.RefKind == RefKind.Out)
            {
                exps.Add($"{paramVar}.Attributes = ParameterAttributes.Out;");
            }
        }

    }
}