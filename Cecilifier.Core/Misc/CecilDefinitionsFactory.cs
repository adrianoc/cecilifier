using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Misc
{
    internal sealed class CecilDefinitionsFactory
    {
        public static string CallSite(ITypeResolver resolver, IFunctionPointerTypeSymbol functionPointer)
        {
            return FunctionPointerTypeBasedCecilType(
                resolver, 
                functionPointer, 
                (hasThis, parameters, returnType) => $"new CallSite({returnType}) {{ {hasThis}, {parameters} }}");
        }

        public static string FunctionPointerType(ITypeResolver resolver, IFunctionPointerTypeSymbol functionPointer)
        {
            return FunctionPointerTypeBasedCecilType(
                resolver, 
                functionPointer, 
                (hasThis, parameters, returnType) => $"new FunctionPointerType() {{ {hasThis}, ReturnType = {returnType}, {parameters} }}");
        }
        
        public static IEnumerable<string> Method(IVisitorContext context, string methodVar, string methodName, string methodModifiers, ITypeSymbol returnType, bool refReturn, IList<TypeParameterSyntax> typeParameters)
        {
            var exps = new List<string>();

            var resolvedReturnType = context.TypeResolver.Resolve(returnType);
            if (refReturn)
                resolvedReturnType = resolvedReturnType.MakeByReferenceType();
            
            // for type parameters we may need to postpone setting the return type (using void as a placeholder, since we need to pass something) until the generic parameters has been
            // handled. This is required because the type parameter may be defined by the method being processed.
            exps.Add($"var {methodVar} = new MethodDefinition(\"{methodName}\", {methodModifiers}, {(returnType.TypeKind == TypeKind.TypeParameter ? context.TypeResolver.Bcl.System.Void : resolvedReturnType)});");
            ProcessGenericTypeParameters(methodVar, context, typeParameters, exps);
            if (returnType.TypeKind == TypeKind.TypeParameter)
            {
                resolvedReturnType = context.TypeResolver.Resolve(returnType);
                exps.Add($"{methodVar}.ReturnType = {(refReturn ? resolvedReturnType.MakeByReferenceType() : resolvedReturnType)};");
            }
            
            return exps;
        }
        
        internal static string Constructor(IVisitorContext context, string ctorLocalVar, string typeName, bool isStatic,string methodAccessibility, string[] paramTypes, string methodDefinitionPropertyValues = null)
        {
            var ctorName = Utils.ConstructorMethodName(isStatic);
            context.DefinitionVariables.RegisterMethod(typeName, ctorName, paramTypes, ctorLocalVar);

            var exp = $@"var {ctorLocalVar} = new MethodDefinition(""{ctorName}"", {methodAccessibility} | MethodAttributes.HideBySig | {Constants.CommonCecilConstants.CtorAttributes}, assembly.MainModule.TypeSystem.Void)";
            if (methodDefinitionPropertyValues != null)
            {
                exp = exp + $"{{ {methodDefinitionPropertyValues} }}";
            }

            return exp + ";";
        }

        /*
         * Creates the snippet for a TypeDefinition.
         * 
         * Note that:
         * 1. At IL level, type parameters from *outer* types are considered to be part of a inner type whence these type parameters need to be added to the list of type parameters even
         *    if the type being declared is not a generic type.
         * 
         * 2. Only type parameters owned by the type being declared are considered when computing the arity of the type (whence the number following the backtick reflects only the
         *    # of the type parameters declared by the type being declared). 
         */
        public static IEnumerable<string> Type(
            IVisitorContext context, 
            string typeVar, 
            string typeName, 
            string attrs, 
            string baseTypeName, 
            string outerTypeName, 
            bool isStructWithNoFields, 
            IEnumerable<string> interfaces, 
            IEnumerable<TypeParameterSyntax> ownTypeParameters, 
            IEnumerable<TypeParameterSyntax> outerTypeParameters, 
            params string[] properties)
        {
            var typeParamList = ownTypeParameters?.ToArray() ?? Array.Empty<TypeParameterSyntax>();
            if (typeParamList.Length > 0)
            {
                typeName = typeName + "`" + typeParamList.Length;
            }
            
            var exps = new List<string>();
            var typeDefExp = $"var {typeVar} = new TypeDefinition(\"{context.CurrentNamespace}\", \"{typeName}\", {attrs}{(!string.IsNullOrWhiteSpace(baseTypeName) ? ", " + baseTypeName : "")})";
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

            var outerTypeVariable = context.DefinitionVariables.GetVariable(outerTypeName, VariableMemberKind.Type);
            if (!string.IsNullOrEmpty(outerTypeName) && outerTypeVariable.IsValid && outerTypeVariable.VariableName != typeVar)
                exps.Add($"{outerTypeVariable.VariableName}.NestedTypes.Add({typeVar});"); // type is a inner type of *context.CurrentType* 
            else
                exps.Add($"assembly.MainModule.Types.Add({typeVar});");

            if (isStructWithNoFields)
            {
                exps.Add($"{typeVar}.ClassSize = 1;");
                exps.Add($"{typeVar}.PackingSize = 0;");
            }
            
            // add type parameters from outer types. 
            var outerTypeParametersArray =  outerTypeParameters?.ToArray() ?? Array.Empty<TypeParameterSyntax>(); 
            ProcessGenericTypeParameters(typeVar, context, outerTypeParametersArray.Concat(typeParamList).ToArray(), exps);
            return exps;
        }
        
        public static IEnumerable<string> Type(IVisitorContext context, string typeVar, string typeName, string outerTypeName,string attrs, string baseTypeName, bool isStructWithNoFields, IEnumerable<string> interfaces, TypeParameterListSyntax typeParameters = null, params string[] properties)
        {
            return Type(
                context,
                typeVar,
                typeName,
                attrs,
                baseTypeName,
                outerTypeName,
                isStructWithNoFields,
                interfaces,
                typeParameters?.Parameters,
                Array.Empty<TypeParameterSyntax>(),
                properties);
        }

        private static string GenericParameter(IVisitorContext context, string typeParameterOwnerVar, string genericParamName, string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)
        {
            context.DefinitionVariables.RegisterNonMethod(typeParameterSymbol.ContainingSymbol.FullyQualifiedName(), genericParamName, VariableMemberKind.TypeParameter, genParamDefVar);
            return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeParameterOwnerVar}){Variance(typeParameterSymbol)};";
        }

        private static string Variance(ITypeParameterSymbol typeParameterSymbol)
        {
            if (typeParameterSymbol.Variance == VarianceKind.In)
            {
                return " { IsContravariant = true }";
            }
            
            if (typeParameterSymbol.Variance == VarianceKind.Out)
            {
                return " { IsCovariant = true }";
            }

            return string.Empty;
        }

        public static IEnumerable<string> Field(IVisitorContext context, string declaringTypeName, string declaringTypeVar, string fieldVar, string name, string fieldType, string fieldAttributes, object constantValue = null)
        {
            context.DefinitionVariables.RegisterNonMethod(declaringTypeName, name, VariableMemberKind.Field, fieldVar);
            var fieldExp = $"var {fieldVar} = new FieldDefinition(\"{name}\", {fieldAttributes}, {fieldType})";
            return new []
            {
                constantValue != null ? $"{fieldExp} {{ Constant = {constantValue} }} ;" : $"{fieldExp};", 
                $"{declaringTypeVar}.Fields.Add({fieldVar});"
            };
        }

        public static string Parameter(string name, RefKind byRef, string resolvedType, string paramAttributes = null)
        {
            paramAttributes ??= Constants.ParameterAttributes.None;
            if (RefKind.None != byRef)
            {
                resolvedType = resolvedType.MakeByReferenceType();
            }

            return  $"new ParameterDefinition(\"{name}\", {paramAttributes}, {resolvedType})";
        }
        
        public static IEnumerable<string> Parameter(string name, RefKind byRef, bool isParams, string methodVar, string paramVar, string resolvedType, string paramAttributes, string defaultParameterValue)
        {
            var exps = new List<string>();

            exps.Add($"var {paramVar} = {Parameter(name, byRef, resolvedType, paramAttributes)};");
            if (isParams)
            {
                exps.Add($"{paramVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof(ParamArrayAttribute).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
            }

            if (defaultParameterValue != null)
                exps.Add($"{paramVar}.Constant = {defaultParameterValue};");

            exps.Add($"{methodVar}.Parameters.Add({paramVar});");

            return exps;
        }
        
        public static IEnumerable<string> Parameter(ParameterSyntax node, SemanticModel semanticModel, string methodVar, string paramVar, string resolvedType, string defaultParameterValue)
        {
            var paramSymbol = semanticModel.GetDeclaredSymbol(node);
            return Parameter(
                node.Identifier.Text, 
                paramSymbol!.RefKind, 
                isParams: node.GetFirstToken().Kind() == SyntaxKind.ParamsKeyword,
                methodVar,
                paramVar,
                resolvedType,
                paramSymbol!.AsParameterAttribute(),
                defaultParameterValue);
        }
        
        public static string Parameter(IParameterSymbol paramSymbol, string resolvedType)
        {
            return Parameter(
                paramSymbol.Name, 
                paramSymbol.RefKind,
                resolvedType,
                paramSymbol.AsParameterAttribute());
        }

        public static IEnumerable<string> Attribute(string attrTargetVar, IVisitorContext context, AttributeSyntax attribute, Func<ITypeSymbol, AttributeArgumentSyntax[], string> ctorResolver)
        {
            var exps = new List<string>();
            var attrType = context.GetTypeInfo(attribute.Name);

            var customAttrVar = context.Naming.CustomAttribute(attribute.Name.ToSimpleName());
            var attributeArguments = attribute.ArgumentList == null
                ? Array.Empty<AttributeArgumentSyntax>()
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
            var genericTypeParamEntries = ArrayPool<(string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)>.Shared.Rent(typeParamList.Count);
            for(int i = 0; i < typeParamList.Count; i++)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(typeParamList[i]);
                var genericParamName = typeParamList[i].Identifier.Text;
                
                var genParamDefVar = context.Naming.GenericParameterDeclaration(typeParamList[i]);
                exps.Add(GenericParameter(context, memberDefVar, genericParamName, genParamDefVar, symbol));
                genericTypeParamEntries[i] = (genParamDefVar, symbol);
            }
            
            for(int i = 0; i < typeParamList.Count; i++)
            {
                AddConstraints(genericTypeParamEntries[i].genParamDefVar, genericTypeParamEntries[i].typeParameterSymbol);
                exps.Add($"{memberDefVar}.GenericParameters.Add({genericTypeParamEntries[i].genParamDefVar});");
            }
            
            ArrayPool<(string genParamDefVar, ITypeParameterSymbol typeParameterSymbol)>.Shared.Return(genericTypeParamEntries);

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
            if (instructions.Length == 0)
                return exps;
            
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
                SyntaxKind.EnumDeclaration => string.Empty,
                _ => throw new Exception("Not supported type declaration: " + syntaxKind)
            };
        }

        private static string FunctionPointerTypeBasedCecilType(ITypeResolver resolver, IFunctionPointerTypeSymbol functionPointer, Func<string, string, string, string> factory)
        {
            var parameters = $"Parameters={{ {string.Join(',', functionPointer.Signature.Parameters.Select(p => Parameter(p, resolver.Resolve(p.Type))))} }}";
            var returnType = resolver.Resolve(functionPointer.Signature.ReturnType);
            return factory("HasThis = false", parameters, returnType);
        }

        public static void InstantiateDelegate(IVisitorContext context, string ilVar, ITypeSymbol delegateType, string variableName)
        {
            context.EmitCilInstruction(ilVar, OpCodes.Ldftn, variableName);
            var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor"); 
            context.EmitCilInstruction(ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(context));
        }
    }
}
