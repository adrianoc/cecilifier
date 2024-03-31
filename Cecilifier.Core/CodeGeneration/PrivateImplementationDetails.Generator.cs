using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.CodeGeneration;

/*
 * - Used to hold array inline initialization.
 * - A single type per assembly
 * - Each array size (in bytes) uses either a nested struct
 *   - Name='__StaticArrayInitTypeSize=[size in bytes]'
 *   - .pack = 1
 *   - .size = [size in bytes]
 * - or Int32 / Int64 for arrays sizes of 4 anf 8 bytes respectively  
 * - A static field is introduced per content of each optimized array initialization 
 * - Initialization of multiple arrays with same size -> reuses struct with corresponding size. 
 * 
 * .class private auto ansi sealed '<PrivateImplementationDetails>'
 * extends [System.Runtime]System.Object
 * {
 *      .class nested private explicit ansi sealed '__StaticArrayInitTypeSize=3' extends [System.Runtime]System.ValueType
 *      {
 *          .pack 1
 *          .size 3
 *      } // end of class __StaticArrayInitTypeSize=3
 *
 *      // Fields
 *      .field assembly static initonly valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=3' '039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81' at I_00002B50
 *      .data cil I_00002B50 = bytearray (01 02 03)
 * } // end of class <PrivateImplementationDetails>
 */
internal partial class PrivateImplementationDetailsGenerator
{
    internal static string GetOrEmmitInlineArrayAsSpanMethod(IVisitorContext context)
    {
        var found = context.DefinitionVariables.GetVariable("InlineArrayAsSpan", VariableMemberKind.Method, Constants.CompilerGeneratedTypes.PrivateImplementationDetails);
        if (found.IsValid)
            return found.VariableName;
        
        var privateImplementationDetailsVar = GetOrCreatePrivateImplementationDetailsTypeVariable(context);
        
        context.WriteNewLine();
        context.WriteComment($"{Constants.CompilerGeneratedTypes.PrivateImplementationDetails}.InlineArrayAsSpan()");
        var methodVar = context.Naming.SyntheticVariable("inlineArrayAsSpan", ElementKind.Method);

        var methodTypeQualifiedName = $"{privateImplementationDetailsVar.MemberName}.InlineArrayAsSpan";
        var methodExpressions = CecilDefinitionsFactory.Method(
                                                        context,
                                                        $"{privateImplementationDetailsVar.MemberName}",
                                                        methodVar, 
                                                        "InlineArrayAsSpan",
                                                        "MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig",
                                                        [ 
                                                            new CecilDefinitionsFactory.ParameterSpec("buffer", "TBuffer", RefKind.Ref, (context, name) => ResolveOwnedGenericParameter(context, name, methodTypeQualifiedName)), 
                                                            new CecilDefinitionsFactory.ParameterSpec("length", context.TypeResolver.Bcl.System.Int32, RefKind.None)
                                                        ],
                                                        ["TBuffer", "TElement"],
                                                        context =>
                                                        {
                                                            var spanTypeParameter = ResolveOwnedGenericParameter(context, "TElement", methodTypeQualifiedName);
                                                            return context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemSpan).MakeGenericInstanceType(spanTypeParameter);
                                                        });

        context.WriteCecilExpressions(methodExpressions);
        
        var tBufferVar = ResolveOwnedGenericParameter(context, "TBuffer", methodTypeQualifiedName);
        var tElementVar = ResolveOwnedGenericParameter(context, "TElement", methodTypeQualifiedName);

        context.WriteComment("Unsafe.As() generic instance method");
        var unsafeAsVar = GetUnsafeAsMethod(context).MethodResolverExpression(context).MakeGenericInstanceMethod(context, "unsafeAs", [tBufferVar, tElementVar]);
        
        context.WriteComment("MemoryMarshal.CreateSpan() generic instance method");
        var memoryMarshalCreateSpanVar = GetMemoryMarshalCreateSpanMethod(context).MethodResolverExpression(context).MakeGenericInstanceMethod(context, "createSpan", [tElementVar]);

        var methodBodyExpressions = CecilDefinitionsFactory.MethodBody(
            methodVar,
            [
                OpCodes.Ldarg_0,
                OpCodes.Call.WithOperand(unsafeAsVar),
                OpCodes.Ldarg_1,
                OpCodes.Call.WithOperand(memoryMarshalCreateSpanVar),
                OpCodes.Ret
            ]);

        var finalExps = methodBodyExpressions.Append($"{privateImplementationDetailsVar.VariableName}.Methods.Add({methodVar});");
        context.WriteCecilExpressions(finalExps);
        
        return methodVar;
        
        static IMethodSymbol GetMemoryMarshalCreateSpanMethod(IVisitorContext context)
        {
            VerifyCreateSpanHasOnlyOneOverload(context);
            var createSpanMethod= context.RoslynTypeSystem.SystemRuntimeInteropServicesMemoryMarshal
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Single(m => m.Name == "CreateSpan" && m.Parameters.Length == 2 && m.Parameters[0].RefKind == RefKind.Ref && m.Parameters[1].Type.Equals(context.RoslynTypeSystem.SystemInt32, SymbolEqualityComparer.Default));

            return createSpanMethod;
        }

        [Conditional("DEBUG")]
        static void VerifyCreateSpanHasOnlyOneOverload(IVisitorContext context)
        {
            var candidates= context.RoslynTypeSystem.SystemRuntimeInteropServicesMemoryMarshal
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.Name == "CreateSpan");
            
            Debug.Assert(candidates.Count() == 1);
        }
    }
    
    public static string GetOrEmmitInlineArrayFirstElementRefMethod(IVisitorContext context)
    {
        var found = context.DefinitionVariables.GetVariable("InlineArrayFirstElementRef", VariableMemberKind.Method, Constants.CompilerGeneratedTypes.PrivateImplementationDetails);
        if (found.IsValid)
            return found.VariableName;
        
        var privateImplementationDetailsVar = GetOrCreatePrivateImplementationDetailsTypeVariable(context);
        
        context.WriteNewLine();
        context.WriteComment($"{Constants.CompilerGeneratedTypes.PrivateImplementationDetails}.InlineArrayFirstElementRef()");
        var methodVar = context.Naming.SyntheticVariable("inlineArrayFirstElementRef", ElementKind.Method);

        var methodTypeQualifiedName = $"{privateImplementationDetailsVar.MemberName}.InlineArrayFirstElementRef";
        var methodExpressions = CecilDefinitionsFactory.Method(
                                                        context,
                                                        $"{privateImplementationDetailsVar.MemberName}",
                                                        methodVar, 
                                                        "InlineArrayFirstElementRef",
                                                        "MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig",
                                                        [ new CecilDefinitionsFactory.ParameterSpec("buffer", "TBuffer", RefKind.Ref, (ctx, name) => ResolveOwnedGenericParameter(ctx, name, methodTypeQualifiedName))],
                                                        ["TBuffer", "TElement"],
                                                        ctx =>
                                                        {
                                                            var spanTypeParameter = ResolveOwnedGenericParameter(ctx, "TElement", methodTypeQualifiedName);
                                                            return spanTypeParameter.MakeByReferenceType();
                                                        });

        context.WriteCecilExpressions(methodExpressions);
        
        var tBufferTypeParameter = ResolveOwnedGenericParameter(context, "TBuffer", methodTypeQualifiedName);
        var tElementTypeParameter = ResolveOwnedGenericParameter(context, "TElement", methodTypeQualifiedName);

        var unsafeAsVarName = GetUnsafeAsMethod(context)
            .MethodResolverExpression(context)
            .MakeGenericInstanceMethod(context, "unsafeAs", [tBufferTypeParameter, tElementTypeParameter]);

        var methodBodyExpressions = CecilDefinitionsFactory.MethodBody(
            methodVar,
            [
                OpCodes.Ldarg_0,
                OpCodes.Call.WithOperand(unsafeAsVarName),
                OpCodes.Ret
            ]);
        
        context.WriteCecilExpressions(methodBodyExpressions);
        
        context.WriteCecilExpression($"{privateImplementationDetailsVar.VariableName}.Methods.Add({methodVar});");
        context.WriteNewLine();
        context.WriteComment("-------------------------------");
        context.WriteNewLine();
        return methodVar;
    }
    
    public static string GetOrEmmitInlineArrayElementRefMethod(IVisitorContext context)
    {
        var found = context.DefinitionVariables.GetVariable("InlineArrayElementRef", VariableMemberKind.Method, Constants.CompilerGeneratedTypes.PrivateImplementationDetails);
        if (found.IsValid)
            return found.VariableName;
        
        var privateImplementationDetailsVar = GetOrCreatePrivateImplementationDetailsTypeVariable(context);
        
        context.WriteNewLine();
        context.WriteComment($"{Constants.CompilerGeneratedTypes.PrivateImplementationDetails}.InlineArrayElementRef()");
        var methodVar = context.Naming.SyntheticVariable("inlineArrayElementRef", ElementKind.Method);

        var methodTypeQualifiedName = $"{privateImplementationDetailsVar.MemberName}.InlineArrayElementRef";
        var methodExpressions = CecilDefinitionsFactory.Method(
                                                        context,
                                                        $"{privateImplementationDetailsVar.MemberName}",
                                                        methodVar, 
                                                        "InlineArrayElementRef",
                                                        "MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig",
                                                        [ 
                                                            new CecilDefinitionsFactory.ParameterSpec("buffer", "TBuffer", RefKind.Ref, (ctx, name) => ResolveOwnedGenericParameter(ctx, name, methodTypeQualifiedName)),
                                                            new CecilDefinitionsFactory.ParameterSpec("index", context.TypeResolver.Bcl.System.Int32, RefKind.None)
                                                        ],
                                                        ["TBuffer", "TElement"],
                                                        ctx =>
                                                        {
                                                            var spanTypeParameter = ResolveOwnedGenericParameter(ctx, "TElement", methodTypeQualifiedName);
                                                            return spanTypeParameter.MakeByReferenceType();
                                                        });

        context.WriteCecilExpressions(methodExpressions);
        
        var tbufferTypeParameter = ResolveOwnedGenericParameter(context, "TBuffer", methodTypeQualifiedName);
        var telementTypeParameter = ResolveOwnedGenericParameter(context, "TElement", methodTypeQualifiedName);

        var unsafeAsVarName = GetUnsafeAsMethod(context)
                                        .MethodResolverExpression(context)
                                        .MakeGenericInstanceMethod(context, "unsafeAs", [tbufferTypeParameter, telementTypeParameter]);
        
        var unsafeAddVarName = GetUnsafeAddMethod(context)
                                        .MethodResolverExpression(context)
                                        .MakeGenericInstanceMethod(context, "unsafeAdd", [telementTypeParameter]);
        
        var methodBodyExpressions = CecilDefinitionsFactory.MethodBody(
            methodVar,
            [
                OpCodes.Ldarg_0,
                OpCodes.Call.WithOperand(unsafeAsVarName),
                OpCodes.Ldarg_1,
                OpCodes.Call.WithOperand(unsafeAddVarName),
                OpCodes.Ret
            ]);
        
        context.WriteCecilExpressions(methodBodyExpressions);
        context.WriteCecilExpression($"{privateImplementationDetailsVar.VariableName}.Methods.Add({methodVar});");
        context.WriteNewLine();
        context.WriteComment("-------------------------------");
        context.WriteNewLine();
        
        return methodVar;
    }
    
    private static string ResolveOwnedGenericParameter(IVisitorContext context, string name, string methodTypeQualifiedName)
    {
        var spanTypeParameter = context.DefinitionVariables.GetVariable(
            name, 
            VariableMemberKind.TypeParameter, 
            methodTypeQualifiedName);

        return spanTypeParameter.VariableName;
    }
    
    internal static string GetOrCreateInitializationBackingFieldVariableName(IVisitorContext context, long sizeInBytes, string arrayElementTypeName, string initializationExpression)
    {
        Span<byte> toBeHashed = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount(initializationExpression)];
        System.Text.Encoding.UTF8.GetBytes(initializationExpression, toBeHashed);
        
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(toBeHashed, hash);

        var fieldName = Convert.ToHexString(hash);
        var found = context.DefinitionVariables.GetVariable(fieldName, VariableMemberKind.Field, Constants.CompilerGeneratedTypes.PrivateImplementationDetails);
        if (found.IsValid)
            return found.VariableName;
        
        var privateImplementationDetailsVar = GetOrCreatePrivateImplementationDetailsTypeVariable(context);
        var rawDataTypeVar = GetOrCreateRawDataType(context, sizeInBytes);
        
        // Add a field to hold the static initialization data.
        //
        //                                                                  field type                                                      Field name = Hash(initialization data)             RVA computed by Cecil
        //                                            +--------------------------------------------------------+                          +-------------------------                          +----------------------
        //                                           /                                                          \                        /                                                   / 
        // .field assembly static initonly valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=3' '039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81' at I_00002B50
        var fieldVar = context.Naming.SyntheticVariable("arrayInitializerData", ElementKind.Field);
        var fieldExpressions = CecilDefinitionsFactory.Field(
                                                                context, 
                                                                privateImplementationDetailsVar.MemberName, 
                                                                privateImplementationDetailsVar.VariableName, 
                                                                fieldVar,
                                                                fieldName,
                                                                rawDataTypeVar,
                                                                Constants.CompilerGeneratedTypes.StaticArrayInitFieldModifiers);
        context.WriteCecilExpressions(fieldExpressions);
        context.WriteCecilExpression($"{fieldVar}.InitialValue = Cecilifier.Runtime.TypeHelpers.ToByteArray<{arrayElementTypeName}>({initializationExpression});");
        context.WriteNewLine();

        return context.DefinitionVariables.GetVariable(fieldName, VariableMemberKind.Field, Constants.CompilerGeneratedTypes.PrivateImplementationDetails);
    }

    private static string GetOrCreateRawDataType(IVisitorContext context, long sizeInBytes)
    {
        if (sizeInBytes == sizeof(int))
            return context.TypeResolver.Bcl.System.Int32;
        
        if (sizeInBytes == sizeof(long))
            return context.TypeResolver.Bcl.System.Int64;
        
        var rawDataHolderStructName = Constants.CompilerGeneratedTypes.StaticArrayInitTypeNameFor(sizeInBytes);
        var found = context.DefinitionVariables.GetVariable(rawDataHolderStructName, VariableMemberKind.Type, Constants.CompilerGeneratedTypes.PrivateImplementationDetails);
        if (found.IsValid)
            return found;

        context.WriteNewLine();
        context.WriteComment($"{rawDataHolderStructName} struct.");
        context.WriteComment($"This struct is emitted by the compiler and is used to hold raw data used in arrays/span initialization optimizations");
        
        var rawDataHolderTypeVar = context.Naming.Type("rawDataTypeVar", ElementKind.Struct);
        var privateImplementationDetails = CecilDefinitionsFactory.Type(
            context, 
            rawDataHolderTypeVar, 
            string.Empty,
            rawDataHolderStructName,
            Constants.CompilerGeneratedTypes.StaticArrayRawDataHolderTypeModifiers, 
            context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemValueType), 
            Constants.CompilerGeneratedTypes.PrivateImplementationDetails,
            isStructWithNoFields: false, 
            Array.Empty<string>(), 
            Array.Empty<TypeParameterSyntax>(),
            Array.Empty<TypeParameterSyntax>(), 
            $"ClassSize = {sizeInBytes}",
            "PackingSize = 1");

        context.WriteCecilExpressions(privateImplementationDetails);

        var rawDataTypeVar = context.DefinitionVariables.RegisterNonMethod(
                                                                Constants.CompilerGeneratedTypes.PrivateImplementationDetails, 
                                                                rawDataHolderStructName, 
                                                                VariableMemberKind.Type, 
                                                                rawDataHolderTypeVar);
        return rawDataTypeVar.VariableName;
    }

    private static DefinitionVariable GetOrCreatePrivateImplementationDetailsTypeVariable(IVisitorContext context)
    {
        var found = context.DefinitionVariables.GetVariable(Constants.CompilerGeneratedTypes.PrivateImplementationDetails, VariableMemberKind.Type, string.Empty);
        if (found.IsValid)
            return found;
        
        context.WriteNewLine();
        context.WriteComment($"{Constants.CompilerGeneratedTypes.PrivateImplementationDetails} class.");
        context.WriteComment($"This type is emitted by the compiler.");
        
        var privateImplementationDetailsVar = context.Naming.Type("privateImplementationDetails", ElementKind.Class);
        var privateImplementationDetails = CecilDefinitionsFactory.Type(
                                                                                    context, 
                                                                                    privateImplementationDetailsVar, 
                                                                                    string.Empty,
                                                                                    Constants.CompilerGeneratedTypes.PrivateImplementationDetails,
                                                                                    Constants.CompilerGeneratedTypes.PrivateImplementationDetailsModifiers, 
                                                                                    context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemObject), 
                                                                                    string.Empty,
                                                                                    isStructWithNoFields: false, 
                                                                                    Array.Empty<string>(), 
                                                                                    Array.Empty<TypeParameterSyntax>(),
                                                                                    Array.Empty<TypeParameterSyntax>());

        context.WriteCecilExpressions(privateImplementationDetails);

        return context.DefinitionVariables.RegisterNonMethod(string.Empty, Constants.CompilerGeneratedTypes.PrivateImplementationDetails, VariableMemberKind.Type, privateImplementationDetailsVar);
    }

    /// <summary>
    /// Encodes rules used by C# compiler to decide whether to apply the array/stackalloc initialization optimization.
    ///
    /// Note that as of version 4.6.1 of Roslyn, the empirically discovered rules are:
    /// 1. Any array initialization expression with length > 2 are optimized
    /// 2. Stackalloc initialization is only considered if the type being allocated is byte/sbyte/bool plus same length rule above
    /// </summary>
    /// <param name="node"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    internal static bool IsApplicableTo(InitializerExpressionSyntax node, IVisitorContext context)
    {
        return node switch
        {
            { RawKind: (int) SyntaxKind.ArrayInitializerExpression } => IsLargeEnoughToWarrantOptimization(node),
            { RawKind: (int) SyntaxKind.ImplicitArrayCreationExpression } => IsLargeEnoughToWarrantOptimization(node),

            { RawKind: (int) SyntaxKind.StackAllocArrayCreationExpression } => IsLargeEnoughToWarrantOptimization(node) && HasCompatibleType(node, context),
            { RawKind: (int) SyntaxKind.ImplicitStackAllocArrayCreationExpression} => IsLargeEnoughToWarrantOptimization(node) && HasCompatibleType(node, context),
            _ => false
        };

        static bool IsLargeEnoughToWarrantOptimization(InitializerExpressionSyntax initializer) => initializer.Expressions.Count > 2;

        // As of Roslyn x, empirically only stackalloc of one byte sized elements are optimized.
        static bool HasCompatibleType(InitializerExpressionSyntax expression, IVisitorContext context)
        {
            var type = context.SemanticModel.GetTypeInfo(expression).Type;
            if (type == null)
                return false;
            
            return type.SpecialType == SpecialType.System_Byte 
                   || type.SpecialType == SpecialType.System_SByte
                   || type.SpecialType == SpecialType.System_Boolean;
        }
    }
}
