#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver.Handles;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.Misc
{
    public sealed class CecilDefinitionsFactory
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

        public static string GenericParameter(IVisitorContext context, string ownerContainingTypeName, string typeParameterOwnerVar, string genericParamName, string genParamDefVar)
        {
            context.DefinitionVariables.RegisterNonMethod(ownerContainingTypeName, genericParamName, VariableMemberKind.TypeParameter, genParamDefVar);
            return $"var {genParamDefVar} = new Mono.Cecil.GenericParameter(\"{genericParamName}\", {typeParameterOwnerVar});";
        }

        public static string ParameterDoesNotHandleParamsKeywordOrDefaultValue(string name, RefKind byRef, string resolvedType, string? paramAttributes = null)
        {
            paramAttributes ??= Constants.ParameterAttributes.None;
            if (RefKind.None != byRef)
            {
                resolvedType = new ResolvedType(resolvedType).MakeByReferenceType();
            }

            return $"new ParameterDefinition(\"{name}\", {paramAttributes}, {resolvedType})";
        }

        public static IEnumerable<string> Parameter(string name, RefKind byRef, string? paramsAttributeTypeName, string methodVar, string paramVar, string resolvedType, string paramAttributes, (string? Value, bool Present) defaultParameterValue)
        {
            var exps = new List<string>();

            exps.Add($"var {paramVar} = {ParameterDoesNotHandleParamsKeywordOrDefaultValue(name, byRef, resolvedType, paramAttributes)};");
            if (!string.IsNullOrWhiteSpace(paramsAttributeTypeName))
            {
                exps.Add($"{paramVar}.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.Import(typeof({paramsAttributeTypeName}).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[0], null))));");
            }

            if (defaultParameterValue.Present)
                exps.Add($"{paramVar}.Constant = {defaultParameterValue.Value ?? "null" };");

            exps.Add($"{methodVar}.Parameters.Add({paramVar});");

            return exps;
        }

        public static IEnumerable<string> Parameter(IVisitorContext context, ParameterSyntax node, string methodVar, string paramVar)
        {
            var paramSymbol = context.SemanticModel.GetDeclaredSymbol(node);
            TypeDeclarationVisitor.EnsureForwardedTypeDefinition(context, paramSymbol!.Type, Array.Empty<TypeParameterSyntax>());
            return Parameter(context, paramSymbol, methodVar, paramVar);
        }

        public static IEnumerable<string> Parameter(IVisitorContext context, IParameterSymbol paramSymbol, string methodVar, string paramVar)
        {
            return Parameter(
                paramSymbol.Name,
                paramSymbol.RefKind,
                paramSymbol.ParamsAttributeMatchingType(),
                methodVar,
                paramVar,
                context.TypeResolver.ResolveAny(paramSymbol.Type, ResolveTargetKind.Parameter, methodVar),
                paramSymbol.AsParameterAttribute(),
                paramSymbol.ExplicitDefaultValue(rawString: false));
        }

        public static string DefaultTypeAttributeFor(TypeKind typeKind, bool hasStaticCtor)
        {
            var basicClassAttrs = "TypeAttributes.AnsiClass" + (hasStaticCtor ? "" : " | TypeAttributes.BeforeFieldInit");
            return typeKind switch
            {
                TypeKind.Struct => "TypeAttributes.Sealed |" + basicClassAttrs,
                TypeKind.Class => basicClassAttrs,
                TypeKind.Interface => "TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.BeforeFieldInit",
                TypeKind.Delegate => "TypeAttributes.Sealed",
                TypeKind.Enum => string.Empty,
                _ => throw new Exception("Not supported type declaration: " + typeKind)
            };
        }

        private static string FunctionPointerTypeBasedCecilType(ITypeResolver resolver, IFunctionPointerTypeSymbol functionPointer, Func<string, string, string, string> factory)
        {
            var parameters = $"Parameters={{ {string.Join(',', functionPointer.Signature.Parameters.Select(p => ParameterDoesNotHandleParamsKeywordOrDefaultValue(p.Name, p.RefKind, resolver.ResolveAny(p.Type))))} }}";
            var returnType = resolver.ResolveAny(functionPointer.Signature.ReturnType);
            return factory("HasThis = false", parameters, returnType);
        }

        public static void InstantiateDelegate(IVisitorContext context, string ilVar, ITypeSymbol delegateType, string targetMethodExp, StaticDelegateCacheContext staticDelegateCacheContext)
        {
            // To match Roslyn implementation we need to cache static method do delegate conversions.
            if (staticDelegateCacheContext.IsStaticDelegate)
            {
                staticDelegateCacheContext.EnsureCacheBackingFieldIsEmitted(context.TypeResolver.ResolveAny(delegateType));
                LogWarningIfStaticMethodIsDeclaredInOtherType(context, staticDelegateCacheContext);

                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldsfld, staticDelegateCacheContext.CacheBackingField);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);

                var cacheAlreadyInitializedTargetVarName = context.Naming.Label("cacheHit");
                context.Generate($"var {cacheAlreadyInitializedTargetVarName} = {ilVar}.Create(OpCodes.Nop);");
                context.WriteNewLine();
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Brtrue, cacheAlreadyInitializedTargetVarName);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Pop);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldnull);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldftn, targetMethodExp);
                var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor");
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(context));
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Stsfld, staticDelegateCacheContext.CacheBackingField);
                context.Generate($"{ilVar}.Append({cacheAlreadyInitializedTargetVarName});");
                context.WriteNewLine();
            }
            else
            {
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldftn, targetMethodExp);
                var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor");
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(context));
            }
        }

        private static void LogWarningIfStaticMethodIsDeclaredInOtherType(IVisitorContext context, StaticDelegateCacheContext staticDelegateCacheContext)
        {
            var currentType = context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
            if (currentType.IsValid && currentType.MemberName != staticDelegateCacheContext.Method.ContainingType.Name)
            {
                context.WriteComment($"*****************************************************************");
                context.WriteComment($"WARNING: Converting static method ({staticDelegateCacheContext.Method.FullyQualifiedName()}) to delegate in a type other than the one defining it may generate incorrect code. Access type: {currentType.MemberName}, Method type: {staticDelegateCacheContext.Method.ContainingType.Name}");
                context.WriteComment($"*****************************************************************");
            }
        }

        public static class Collections
        {
            /// <summary>
            /// When passing some types of params parameters Cecilifier needs to generate code to instantiate a List{T} and populate its values.
            /// In addition to that this method introduces a local variable of type <see cref="System.Span{T}"/> and initializes it with a
            /// reference to the instantiated <see cref="System.Collections.Generic.List{T}"/>.
            ///
            /// Callers can use that variable to initialize the list in a performant way.  
            /// </summary>
            /// <param name="context"></param>
            /// <param name="listOfTTypeSymbol"><see cref="ITypeSymbol"/> for the List{T}.</param>
            /// <param name="elementCount">Number of elements to be stored.</param>
            public static (DefinitionVariable, string) InstantiateListToStoreElements(IVisitorContext context, string ilVar, INamedTypeSymbol listOfTTypeSymbol, int elementCount)
            {
                var resolvedListTypeArgument = context.TypeResolver.ResolveAny(listOfTTypeSymbol.TypeArguments[0]);

                context.WriteNewLine();
                context.WriteComment("Instantiates a List<T> passing the # of elements to its ctor.");
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldc_I4, elementCount);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, listOfTTypeSymbol.Constructors.First(ctor => ctor.Parameters.Length == 1).MethodResolverExpression(context));

                // Pushes an extra copy of the reference to the list instance into the stack
                // to avoid introducing a local variable. This will be left at the top of the stack
                // when the initialization code finishes.
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);

                // Calls 'CollectionsMarshal.SetCount(list, num)' on the list.
                var collectionMarshalTypeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(System.Runtime.InteropServices.CollectionsMarshal).FullName!).EnsureNotNull();
                var setCountMethod = collectionMarshalTypeSymbol.GetMembers("SetCount").OfType<IMethodSymbol>().Single().MethodResolverExpression(context).MakeGenericInstanceMethod(context, "SetCount", [ resolvedListTypeArgument ]); 
                
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldc_I4, elementCount);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Call, setCountMethod);
                
                context.WriteNewLine();
                context.WriteComment("Add a Span<T> local variable and initialize it with `CollectionsMarshal.AsSpan(list)`");
                var spanToList = context.AddLocalVariableToCurrentMethod(
                    "listSpan", 
                    context.TypeResolver.ResolveAny(context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(resolvedListTypeArgument));

                context.ApiDriver.WriteCilInstruction(context, ilVar, 
                    OpCodes.Call, 
                    collectionMarshalTypeSymbol.GetMembers("AsSpan").OfType<IMethodSymbol>().Single().MethodResolverExpression(context).MakeGenericInstanceMethod(context, "AsSpan", [ resolvedListTypeArgument ]));
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Stloc, new CilLocalVariableHandle(spanToList.VariableName));
                
                return (spanToList, resolvedListTypeArgument);
            }
            
            public static string GetSpanIndexerGetter(IVisitorContext context, string typeArgument)
            {
                var methodVar = context.Naming.SyntheticVariable("getItem", ElementKind.Method);
                var declaringType = context.TypeResolver.ResolveAny(context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(typeArgument);
                context.Generate($$"""var {{methodVar}} = new MethodReference("get_Item", {{context.TypeResolver.Bcl.System.Void}}, {{declaringType}}) { HasThis = true, ExplicitThis = false };""");
                context.WriteNewLine();
                context.Generate($"{methodVar}.Parameters.Add(new ParameterDefinition({context.TypeResolver.Bcl.System.Int32}));");
                context.WriteNewLine();
                context.Generate($"""{methodVar}.ReturnType = ((GenericInstanceType) {methodVar}.DeclaringType).ElementType.GenericParameters[0].MakeByReferenceType();""");
                context.WriteNewLine();

                return methodVar;
            }
        }
    }
}
