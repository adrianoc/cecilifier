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
        public static IEnumerable<string> Method(IVisitorContext context, string methodVar, string methodName, string methodModifiers, string returnType, IList<TypeParameterSyntax> typeParameters)
        {
            var exps = new List<string>(); 
            exps.Add($"var {methodVar} = new MethodDefinition(\"{methodName}\", {methodModifiers}, {returnType});");
            ProcessGenericTypeParameters(methodVar, context, typeParameters, exps);

            return exps;
        }
        
        public static string Constructor(IVisitorContext context, out string ctorLocalVar, string typeName, string methodAccessibility, string[] paramTypes, string methodDefinitionPropertyValues = null)
        {
            ctorLocalVar = MethodExtensions.LocalVariableNameFor(typeName, "ctor", "");
            context.DefinitionVariables.RegisterMethod(typeName, ".ctor", paramTypes, ctorLocalVar);

            var exp = $@"var {ctorLocalVar} = new MethodDefinition("".ctor"", {methodAccessibility} | MethodAttributes.HideBySig | {ConstructorDeclarationVisitor.CtorFlags}, assembly.MainModule.TypeSystem.Void)";
            if (methodDefinitionPropertyValues != null)
            {
                exp = exp + $"{{ {methodDefinitionPropertyValues} }}";
            }

            return exp + ";";
        }

        public static IEnumerable<string> Type(IVisitorContext context, string typeVar, string typeName, string attrs, string baseTypeName, bool isStructWithNoFields, IEnumerable<string> interfaces, TypeParameterListSyntax typeParameters = null, params string[] properties)
        {
            var typeParamList = typeParameters?.Parameters.ToArray() ?? Array.Empty<TypeParameterSyntax>();
            if (typeParamList.Length > 0)
            {
                typeName = typeName + "`" + typeParamList.Length;
            }
            
            var exps = new List<string>();
            var typeDefExp = $"var {typeVar} = new TypeDefinition(\"{context.Namespace}\", \"{typeName}\", {attrs}{(!string.IsNullOrWhiteSpace(baseTypeName) ? ", " + baseTypeName : "")})";
            if (properties.Length > 0)
            {
                exps.Add($"{typeDefExp} {{ {string.Join(',', properties)} }};");
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

            ProcessGenericTypeParameters(typeVar, context, typeParamList, exps);
            return exps;
        }

        public static string GenericParameter(IVisitorContext context, string typeVar, string genericParamName, string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)
        {
            context.DefinitionVariables.RegisterNonMethod(string.Empty, genericParamName, MemberKind.Type, genParamDefVar);
            return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeVar}) {Variance(typeParameterSymbol)};";
        }

        private static string  Variance(ITypeParameterSymbol typeParameterSymbol)
        {
            if (typeParameterSymbol.Variance == VarianceKind.In)
            {
                return "{ IsContravariant = true }";
            }
            
            if (typeParameterSymbol.Variance == VarianceKind.Out)
            {
                return "{ IsCovariant = true }";
            }

            return string.Empty;
        }

        public static IEnumerable<string> Field(string declaringTypeVar, string fieldVar, string name, string fieldType, string fieldAttributes, params string[] properties)
        {
            var exps = new List<string>();
            var fieldExp = $"var {fieldVar} = new FieldDefinition(\"{name}\", {fieldAttributes}, {fieldType})";
            if (properties.Length > 0)
            {
                exps.Add($"{fieldExp} {{ {string.Join(',', properties)} }};");
            }
            else
            {
                exps.Add($"{fieldExp};");
            }

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
                exps.Add(
                    $"{paramVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(ParamArrayAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
            }

            exps.Add($"{methodVar}.Parameters.Add({paramVar});");

            return exps;
        }

        public static IEnumerable<string> Attribute(string typeVar, IVisitorContext context, AttributeSyntax attribute, Func<ITypeSymbol, AttributeArgumentSyntax[], string> ctorResolver)
        {
            var exps = new List<string>();
            var attrType = context.GetTypeInfo(attribute.Name);

            var customAttrVar = $"ca_{context.NextLocalVariableTypeId()}";

            var attributeArguments = attribute.ArgumentList == null
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

            string CustomAttributeArgument(TypeInfo argType, AttributeArgumentSyntax attrArg)
            {
                return $"new CustomAttributeArgument(assembly.MainModule.ImportReference(typeof({argType.Type.FullyQualifiedName()})), {attrArg.Expression.EvaluateConstantExpression(context.SemanticModel)})";
            }
        }

        private static void ProcessGenericTypeParameters(string memberDefVar, IVisitorContext context, IList<TypeParameterSyntax> typeParamList, IList<string> exps)
        {
            foreach (var typeParameter in typeParamList)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(typeParameter);
                var genericParamName = typeParameter.Identifier.Text;
                
                var genParamDefVar = $"{memberDefVar}_{genericParamName}";
                
                context.DefinitionVariables.RegisterNonMethod(string.Empty, genericParamName, MemberKind.Type, genParamDefVar);
                exps.Add(GenericParameter(context, memberDefVar, genericParamName, genParamDefVar, symbol));
                AddConstraints(genParamDefVar, symbol);
                exps.Add($"{memberDefVar}.GenericParameters.Add({genParamDefVar});");
            }
            
            void AddConstraints(string genParamDefVar, ITypeParameterSymbol typeParam)
            {
                if (typeParam.HasConstructorConstraint || typeParam.HasValueTypeConstraint) // struct constraint implies new()
                {
                    exps.Add($"{genParamDefVar}.HasDefaultConstructorConstraint = true;");
                }

                if (typeParam.HasReferenceTypeConstraint)
                {
                    exps.Add($"{genParamDefVar}.HasReferenceTypeConstraint = true;");
                }
                
                if (typeParam.HasValueTypeConstraint)
                {
                    var systemValueTypeRef = Utils.ImportFromMainModule("typeof(System.ValueType)");
                    var constraintType = typeParam.HasUnmanagedTypeConstraint
                        ? $"{systemValueTypeRef}.MakeRequiredModifierType({context.TypeResolver.Resolve("System.Runtime.InteropServices.UnmanagedType")})" 
                        : systemValueTypeRef;

                    exps.Add($"{genParamDefVar}.Constraints.Add(new GenericParameterConstraint({constraintType}));");
                    exps.Add($"{genParamDefVar}.HasNotNullableValueTypeConstraint = true;");
                }

                if (typeParam.Variance == VarianceKind.In)
                {
                    exps.Add($"{genParamDefVar}.IsContravariant = true;");
                }
                else if (typeParam.Variance == VarianceKind.Out)
                {
                    exps.Add($"{genParamDefVar}.IsCovariant = true;");
                }
 
                //TODO: symbol.HasNotNullConstraint causes no difference in the generated assembly?
                foreach (var type in typeParam.ConstraintTypes)
                {
                    exps.Add($"{genParamDefVar}.Constraints.Add(new GenericParameterConstraint({context.TypeResolver.Resolve(type)}));");
                }
            }
        }
        
        public static IEnumerable<string> MethodBody(string methodVar, InstructionRepresentation[] instructions)
        {
            var tagToInstructionDefMapping = new Dictionary<string, string>();
            var exps = new List<string>();
            exps.Add($"{methodVar}.Body = new MethodBody({methodVar});");
            
            var ilVar = $"{methodVar}_il";
            exps.Add($"var {ilVar} = {methodVar}.Body.GetILProcessor();");
            var methodInstVar = $"{methodVar}_inst";
            exps.Add($"var {methodInstVar} = {methodVar}.Body.Instructions;");
            
            foreach (var inst in instructions)
            {
                var instVar = "_";
                if (inst.tag != null)
                {
                    instVar = $"{inst.tag}_inst_{instructions.GetHashCode()}";
                    exps.Add($"Instruction {instVar};");
                    tagToInstructionDefMapping[inst.tag] = instVar;
                }

                var operand = inst.operand?.Insert(0, ", ") 
                                ?? inst.branchTargetTag?.Replace(inst.branchTargetTag, $", {tagToInstructionDefMapping[inst.branchTargetTag]}")
                                ?? string.Empty;
                
                exps.Add($"{methodInstVar}.Add({instVar} = {ilVar}.Create({inst.opCode.ConstantName()}{operand}));");
                
            }

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
