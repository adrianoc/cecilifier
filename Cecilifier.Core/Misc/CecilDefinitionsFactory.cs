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
            return Constructor(context, ctorLocalVar, typeName, methodAccessibility, paramTypes, methodDefinitionPropertyValues);
        }

        internal static string Constructor(IVisitorContext context, string ctorLocalVar, string typeName, string methodAccessibility, string[] paramTypes, string methodDefinitionPropertyValues = null)
        {
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

        private static string GenericParameter(IVisitorContext context, string typeVar, string genericParamName, string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)
        {
            context.DefinitionVariables.RegisterNonMethod(string.Empty, genericParamName, MemberKind.Type, genParamDefVar);
            return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeVar}) {Variance(typeParameterSymbol)};";
        }

        private static string Variance(ITypeParameterSymbol typeParameterSymbol)
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

        public static IEnumerable<string> Parameter(string name, RefKind byRef, bool isParams, bool isArray, SemanticModel semanticModel, string methodVar, string paramVar, string resolvedType)
        {
            var exps = new List<string>();
            if (RefKind.None != byRef)
            {
                resolvedType = "new ByReferenceType(" + resolvedType + ")";
            }

            exps.Add($"var {paramVar} = new ParameterDefinition(\"{name}\", ParameterAttributes.None, {resolvedType});");

            AddExtraAttributes(exps, paramVar, byRef);

            if (isParams)
            {
                exps.Add($"{paramVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(ParamArrayAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
            }

            exps.Add($"{methodVar}.Parameters.Add({paramVar});");

            return exps;
        }
        
        public static IEnumerable<string> Parameter(ParameterSyntax node, SemanticModel semanticModel, string methodVar, string paramVar, string resolvedType)
        {
            var paramSymbol = semanticModel.GetDeclaredSymbol(node);
            return Parameter(
                node.Identifier.Text, 
                paramSymbol.RefKind, 
                isParams: node.GetFirstToken().Kind() == SyntaxKind.ParamsKeyword, 
                isArray: false, 
                semanticModel,
                methodVar,
                paramVar,
                resolvedType);
        }

        public static IEnumerable<string> Attribute(string attrTargetVar, IVisitorContext context, AttributeSyntax attribute, Func<ITypeSymbol, AttributeArgumentSyntax[], string> ctorResolver)
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

                ProcessAttributeNamedArguments(SymbolKind.Property, "Properties");
                ProcessAttributeNamedArguments(SymbolKind.Field, "Fields");
            }

            exps.Add($"{attrTargetVar}.CustomAttributes.Add({customAttrVar});");

            return exps;

            string CustomAttributeArgument(TypeInfo argType, AttributeArgumentSyntax attrArg)
            {
                return $"new CustomAttributeArgument({context.TypeResolver.Resolve(argType.Type)}, {attrArg.Expression.EvaluateAsCustomAttributeArgument(context)})";
            }

            void ProcessAttributeNamedArguments(SymbolKind symbolKind, string container)
            {
                var attrMemberNames = attrType.Type.GetMembers().Where(m => m.Kind == symbolKind).Select(m => m.Name).ToHashSet();
                foreach (var namedArgument in attribute.ArgumentList.Arguments.Except(attributeArguments).Where(arg => attrMemberNames.Contains(arg.NameEquals.Name.Identifier.Text)))
                {
                    var argType = context.GetTypeInfo(namedArgument.Expression);
                    exps.Add($"{customAttrVar}.{container}.Add(new CustomAttributeNamedArgument(\"{namedArgument.NameEquals.Name.Identifier.ValueText}\", {CustomAttributeArgument(argType, namedArgument)}));");
                }
            }
        }

        private static void ProcessGenericTypeParameters(string memberDefVar, IVisitorContext context, IList<TypeParameterSyntax> typeParamList, IList<string> exps)
        {
            // forward declare all generic type parameters to allow one type parameter to reference any of the others; this is useful in constraints for example:
            // class Foo<T,S> where T: S  { }
            var tba = new List<(string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)>();
            foreach (var typeParameter in typeParamList)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(typeParameter);
                var genericParamName = typeParameter.Identifier.Text;
                
                var genParamDefVar = $"{memberDefVar}_{genericParamName}";

                // TODO: Generic parameters should be removed as soon as their declaring member gets out of scope. It looks like they being kept around
                // In the same way, local variables should also be removed.
                context.DefinitionVariables.RegisterNonMethod(string.Empty, genericParamName, MemberKind.Type, genParamDefVar);
                exps.Add(GenericParameter(context, memberDefVar, genericParamName, genParamDefVar, symbol));
                
                tba.Add((genParamDefVar, symbol));
            }
            
            foreach (var entry in tba)
            {
                AddConstraints(entry.genParamDefVar, entry.typeParameterSymbol);
                exps.Add($"{memberDefVar}.GenericParameters.Add({entry.genParamDefVar});");
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
            var ilVar = $"{methodVar}_il";
            return MethodBody(methodVar, ilVar, instructions);
        }

        public static IEnumerable<string> MethodBody(string methodVar, string ilVar, InstructionRepresentation[] instructions)
        {
            var tagToInstructionDefMapping = new Dictionary<string, string>();
            var exps = new List<string>();
            exps.Add($"{methodVar}.Body = new MethodBody({methodVar});");

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
        
        public static string DefaultTypeAttributeFor(SyntaxKind syntaxKind, bool hasStaticCtor)
        {
            var basicClassAttrs = "TypeAttributes.AnsiClass" + (hasStaticCtor ? "" : " | TypeAttributes.BeforeFieldInit");
            return syntaxKind switch
            {
                SyntaxKind.StructDeclaration => "TypeAttributes.SequentialLayout | TypeAttributes.Sealed |" + basicClassAttrs,
                SyntaxKind.ClassDeclaration => basicClassAttrs,
                SyntaxKind.InterfaceDeclaration => "TypeAttributes.Interface | TypeAttributes.Abstract",
                SyntaxKind.DelegateDeclaration => "TypeAttributes.Sealed",
                SyntaxKind.EnumDeclaration => throw new NotImplementedException(),
                _ => throw new Exception("Not supported type declaration: " + syntaxKind)
            };
        }

        private static void AddExtraAttributes(IList<string> exps, string paramVar, RefKind byRef)
        {
            if (byRef == RefKind.Out)
            {
                exps.Add($"{paramVar}.Attributes = ParameterAttributes.Out;");
            }
        }
    }
}
