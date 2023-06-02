using System;
using System.Security.Cryptography;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
internal class PrivateImplementationDetailsGenerator
{
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
        foreach (var exp in fieldExpressions)
        {
            context.WriteCecilExpression(exp);
            context.WriteNewLine();
        }
        
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

        foreach (var exp in privateImplementationDetails)
        {
            context.WriteCecilExpression(exp);
            context.WriteNewLine();
        }

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

        foreach (var exp in privateImplementationDetails)
        {
            context.WriteCecilExpression(exp);
            context.WriteNewLine();
        }

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
