using System.Collections.Generic;
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
            ctorLocalVar = MethodExtensions.LocalVariableNameFor(typeName, new[] {"ctor", ""});

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

            if (context.CurrentType == null)
            {
                exps.Add($"assembly.MainModule.Types.Add({typeVar});");
            }
            else
            {
                // type is a inner type of *context.CurrentType* 
                exps.Add($"{context.ResolveTypeLocalVariable(context.CurrentType)}.NestedTypes.Add({typeVar});");
            }

            if (isStructWithNoFields)
            {
                exps.Add($"{typeVar}.ClassSize = 1;");	
                exps.Add($"{typeVar}.PackingSize = 0;");	
            }

            context.RegisterTypeLocalVariable(typeName, typeVar);
            
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
        
        private static void AddExtraAttributes(IList<string> exps, string paramVar, IParameterSymbol symbol)
        {
            if (symbol.RefKind == RefKind.Out)
            {
                exps.Add($"{paramVar}.Attributes = ParameterAttributes.Out;");
            }
        }

    }
}