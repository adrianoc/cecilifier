using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

internal static class CollectionExpressionProcessor
{
    public static void Process(ExpressionVisitor visitor, CollectionExpressionSyntax node)
    {
        var targetTypeSymbol = visitor.Context.GetTypeInfo(node).ConvertedType.EnsureNotNull();
        if (targetTypeSymbol is IArrayTypeSymbol arrayType)
        {
            HandleAssignmentToArray(visitor, node, arrayType);
        }
        else if (SymbolEqualityComparer.Default.Equals(targetTypeSymbol.OriginalDefinition, visitor.Context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(List<>).FullName!)))
        {
            HandleAssignmentToList(visitor, node, (INamedTypeSymbol) targetTypeSymbol);
        }
        else
        {
            HandleAssignmentToSpan(visitor, node, (INamedTypeSymbol) targetTypeSymbol);
        }
    }

    private static void HandleAssignmentToList(ExpressionVisitor visitor, CollectionExpressionSyntax node, INamedTypeSymbol listOfTTypeSymbol)
    {
        var context = visitor.Context;
        var resolvedListTypeArgument = context.TypeResolver.Resolve(listOfTTypeSymbol.TypeArguments[0]);

        context.WriteNewLine();
        context.WriteComment("Instantiates a List<T> passing the # of elements in the collection expression to its ctor.");
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, node.Elements.Count);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Newobj, listOfTTypeSymbol.Constructors.First(ctor => ctor.Parameters.Length == 1).MethodResolverExpression(visitor.Context));

        // Pushes an extra copy of the reference to the list instance into the stack
        // to avoid introducing a local variable. This will be left at the top of the stack
        // when the initialization code finishes.
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Dup);

        // Calls 'CollectionsMarshal.SetCount(list, num)' on the list.
        var collectionMarshalTypeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(CollectionsMarshal).FullName!);
        var setCountMethod = collectionMarshalTypeSymbol.GetMembers("SetCount").OfType<IMethodSymbol>().Single().MethodResolverExpression(context).MakeGenericInstanceMethod(context, "SetCount", [ resolvedListTypeArgument ]); 
        
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Dup);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, node.Elements.Count);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, setCountMethod);
        
        context.WriteNewLine();
        context.WriteComment("Add a Span<T> local variable and initialize it with `CollectionsMarshal.AsSpan(list)`");
        var spanToList = context.AddLocalVariableToCurrentMethod(
            "listSpan", 
            context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(resolvedListTypeArgument));

        context.EmitCilInstruction(visitor.ILVariable, 
            OpCodes.Call, 
            collectionMarshalTypeSymbol.GetMembers("AsSpan").OfType<IMethodSymbol>().Single().MethodResolverExpression(context).MakeGenericInstanceMethod(context, "AsSpan", [ resolvedListTypeArgument ]));
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Stloc, spanToList.VariableName);

        context.WriteNewLine();
        context.WriteComment($"Initialize each list element through the span (variable '{spanToList.VariableName}')");
        var index = 0;
        var spanGetItemMethod = GetSpanIndexerGetMethod(context, resolvedListTypeArgument);
        var stindOpCode = listOfTTypeSymbol.TypeArguments[0].StindOpCodeFor();
        var targetElementType = stindOpCode == OpCodes.Stobj ? resolvedListTypeArgument : null; // Stobj expects the type of the object being stored.
        
        var collectionExpressionOperation = context.SemanticModel.GetOperation(node).EnsureNotNull<IOperation, ICollectionExpressionOperation>();
        foreach (var element in node.Elements)
        {
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, spanToList.VariableName);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, index);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, spanGetItemMethod);
            visitor.Visit(element);
            context.TryApplyConversions(visitor.ILVariable, collectionExpressionOperation.Elements[index]);
            context.EmitCilInstruction(visitor.ILVariable, stindOpCode, targetElementType);
            index++;
        }
    }
   
    private static void HandleAssignmentToSpan(ExpressionVisitor visitor, CollectionExpressionSyntax node, INamedTypeSymbol spanTypeSymbol)
    {
        Debug.Assert(SymbolEqualityComparer.Default.Equals(spanTypeSymbol.OriginalDefinition, visitor.Context.RoslynTypeSystem.SystemSpan));
     
        var context = visitor.Context;
        var inlineArrayVar = GetOrEmitSyntheticInlineArrayFor(node, context);
        
        var currentMethodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method).VariableName;
        var inlineArrayElementType = spanTypeSymbol.TypeArguments[0];
        var inlineArrayLocalVar = context.Naming.SyntheticVariable("buffer", ElementKind.LocalVariable);
        var inlineArrayTypeVar = inlineArrayVar.MakeGenericInstanceType(context.TypeResolver.Resolve(inlineArrayElementType));
        context.WriteCecilExpression($"var {inlineArrayLocalVar} = {CecilDefinitionsFactory.LocalVariable(inlineArrayTypeVar)};\n");
        context.WriteCecilExpression($"{currentMethodVar}.Body.Variables.Add({inlineArrayLocalVar});\n");
        
        // Initializes the inline array
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, inlineArrayLocalVar);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Initobj, inlineArrayTypeVar);
        
        var inlineArrayElementRefMethodVar = PrivateImplementationDetailsGenerator
                                                        .GetOrEmmitInlineArrayElementRefMethod(context)
                                                        .MakeGenericInstanceMethod(context, "InlineArrayElementRef", [$"{inlineArrayLocalVar}.VariableType", context.TypeResolver.Resolve(spanTypeSymbol.TypeArguments[0])]);
        
        var storeOpCode = inlineArrayElementType.StindOpCodeFor();
        var targetElementType = storeOpCode == OpCodes.Stobj ? context.TypeResolver.Resolve(inlineArrayElementType) : null; // Stobj expects the type of the object being stored.
        var collectionExpressionOperation = context.SemanticModel.GetOperation(node).EnsureNotNull<IOperation, ICollectionExpressionOperation>();
        var index = 0;
        foreach (var element in node.Elements)
        {
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, inlineArrayLocalVar);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, index);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, inlineArrayElementRefMethodVar);
            visitor.Visit(element);
            context.TryApplyConversions(visitor.ILVariable, collectionExpressionOperation.Elements[index]);
            context.EmitCilInstruction(visitor.ILVariable, storeOpCode, targetElementType);
            index++;
        }
        
        // convert the initialized InlineArray to a span and put it in the stack.
        var inlineArrayAsSpanMethodVar = PrivateImplementationDetailsGenerator
                                                    .GetOrEmmitInlineArrayAsSpanMethod(context)
                                                    .MakeGenericInstanceMethod(context, "InlineArrayAsSpan", [$"{inlineArrayLocalVar}.VariableType", context.TypeResolver.Resolve(spanTypeSymbol.TypeArguments[0])]);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, inlineArrayLocalVar);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, node.Elements.Count);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, inlineArrayAsSpanMethodVar);
    }

    private static string GetOrEmitSyntheticInlineArrayFor(CollectionExpressionSyntax node, IVisitorContext context)
    {
        context.WriteNewLine();
        context.WriteComment($"Declares an inline array for backing the data for the collection expression: {node.SourceDetails()}");
        
        var typeVar = context.Naming.Type("", ElementKind.Struct);
        var typeParameterVar = context.Naming.SyntheticVariable("TElementType", ElementKind.GenericParameter);

        string[] typeExps = 
        [
            //[StructLayout(LayoutKind.Auto)]
            $"""var {typeVar} = new TypeDefinition(string.Empty, "<>y_InlineArray{node.Elements.Count}`1", TypeAttributes.NotPublic | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.Sealed, {context.TypeResolver.Bcl.System.ValueType});""",
            CecilDefinitionsFactory.GenericParameter(context, $"{typeVar}.Name", typeVar, "TElementType", typeParameterVar),
            $"""{typeVar}.GenericParameters.Add({typeParameterVar});"""
        ];

        var fieldVar = context.Naming.SyntheticVariable("_element0", ElementKind.Field);
        var fieldExps = CecilDefinitionsFactory.Field(
            context, 
            $"{typeVar}.Name", 
            typeVar,
            fieldVar, 
            "_element0", 
            typeParameterVar, 
            "FieldAttributes.Private");
        
        context.WriteCecilExpressions(typeExps);
        
        //[InlineArray(2)]
        context.WriteCecilExpressions(
            CecilDefinitionsFactory.Attribute(
                "inlineArray", 
                typeVar, 
                context,
                ConstructorFor<InlineArrayAttribute>(context, typeof(int)),
                (context.TypeResolver.Bcl.System.Int32, node.Elements.Count.ToString())));
        
        context.WriteCecilExpressions(fieldExps);
        context.AddCompilerGeneratedAttributeTo(fieldVar);
        context.WriteCecilExpression($"assembly.MainModule.Types.Add({typeVar});\n");

        return typeVar;
    }

    private static string ConstructorFor<TType>(IVisitorContext context, params Type[] ctorParamTypes)
    {
        var typeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(TType).FullName!).EnsureNotNull();
        var ctors = typeSymbol.Constructors.Where(ctor => ctor.Parameters.Length == ctorParamTypes.Length);

        if (ctors.Count() == 1)
            return ctors.First().MethodResolverExpression(context);

        var expectedParamTypes = ctorParamTypes.Select(paramType => context.SemanticModel.Compilation.GetTypeByMetadataName(paramType.FullName!)).ToHashSet(SymbolEqualityComparer.Default);
        return ctors.Single(ctor => !ctor.Parameters.Select(p => p.Type).ToHashSet(SymbolEqualityComparer.Default).Except(expectedParamTypes, SymbolEqualityComparer.Default).Any()).MethodResolverExpression(context);
    }

    private static void HandleAssignmentToArray(ExpressionVisitor visitor, CollectionExpressionSyntax node, IArrayTypeSymbol arrayTypeSymbol)
    {
        visitor.Context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, node.Elements.Count);
        visitor.Context.EmitCilInstruction(visitor.ILVariable, OpCodes.Newarr, visitor.Context.TypeResolver.Resolve(arrayTypeSymbol.ElementType));
            
        if (PrivateImplementationDetailsGenerator.IsApplicableTo(node, visitor.Context))
            ArrayInitializationProcessor.InitializeOptimized(visitor, arrayTypeSymbol.ElementType, node.Elements);
        else
            ArrayInitializationProcessor.InitializeUnoptimized<CollectionElementSyntax>(visitor, arrayTypeSymbol.ElementType, node.Elements, visitor.Context.SemanticModel.GetOperation(node));
    }
    
    private static string GetSpanIndexerGetMethod(IVisitorContext context, string typeArgument)
    {
        var methodVar = context.Naming.SyntheticVariable("getItem", ElementKind.Method);
        var declaringType = context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(typeArgument);
        context.WriteCecilExpression($$"""var {{methodVar}} = new MethodReference("get_Item", {{context.TypeResolver.Bcl.System.Void}}, {{declaringType}}) { HasThis = true, ExplicitThis = false };""");
        context.WriteNewLine();
        context.WriteCecilExpression($"{methodVar}.Parameters.Add(new ParameterDefinition({context.TypeResolver.Bcl.System.Int32}));");
        context.WriteNewLine();
        context.WriteCecilExpression($"""{methodVar}.ReturnType = ((GenericInstanceType) {methodVar}.DeclaringType).ElementType.GenericParameters[0].MakeByReferenceType();""");
        context.WriteNewLine();

        return methodVar;
    }
}
