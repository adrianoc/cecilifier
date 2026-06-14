using System.Linq;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver.Handles;
using Microsoft.CodeAnalysis;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.Misc;

public struct CodeGenerationHelpers
{
    internal static DefinitionVariable StoreTopOfStackInLocalVariable(IVisitorContext context, IlContext ilVar, string variableName, ITypeSymbol type)
    {
        var methodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        var resolvedVarType = context.TypeResolver.Resolve(type, ResolveTargetKind.LocalVariable);
        var tempLocalDefinitionVariable = context.ApiDefinitionsFactory.LocalVariable(context, variableName, methodVar.VariableName, resolvedVarType);
        context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Stloc, new CilLocalVariableHandle(tempLocalDefinitionVariable.VariableName));
        return tempLocalDefinitionVariable;
    }

    internal static void InstantiateDelegate(IVisitorContext context, IlContext ilVar, ITypeSymbol delegateType, string targetMethodExp, StaticDelegateCacheContext staticDelegateCacheContext)
    {
        // To match Roslyn implementation we need to cache static method do delegate conversions.
        if (staticDelegateCacheContext.IsStaticDelegate)
        {
            staticDelegateCacheContext.EnsureCacheBackingFieldIsEmitted(context.TypeResolver.Resolve(delegateType, ResolveTargetKind.Field));
            LogWarningIfStaticMethodIsDeclaredInOtherType(context, staticDelegateCacheContext);

            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldsfld, staticDelegateCacheContext.CacheBackingField.AsToken());
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);

            var cacheAlreadyInitializedTargetVarName = context.Naming.Label("cacheHit");
            context.ApiDriver.DefineLabel(context, ilVar, cacheAlreadyInitializedTargetVarName);
            
            context.WriteNewLine();
            context.ApiDriver.WriteCilBranch(context, ilVar, OpCodes.Brtrue, cacheAlreadyInitializedTargetVarName);
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Pop);
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldnull);
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldftn, targetMethodExp.AsToken());
            var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor");
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(context).AsToken());
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Dup);
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Stsfld, staticDelegateCacheContext.CacheBackingField.AsToken());
            
            context.ApiDriver.MarkLabel(context, ilVar, cacheAlreadyInitializedTargetVarName);
            context.WriteNewLine();
        }
        else
        {
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldftn, targetMethodExp.AsToken());
            var delegateCtor = delegateType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == ".ctor");
            context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Newobj, delegateCtor.MethodResolverExpression(context).AsToken());
        }
    }

    private static void LogWarningIfStaticMethodIsDeclaredInOtherType(IVisitorContext context, StaticDelegateCacheContext staticDelegateCacheContext)
    {
        var currentType = context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
        if (currentType.IsValid && currentType.MemberName != staticDelegateCacheContext.Method.ContainingType.Name)
        {
            context.EmitWarning(
                $"Converting static method ({staticDelegateCacheContext.Method.FullyQualifiedName()}) to delegate in a type other than the one defining it may generate incorrect code. Access type: {currentType.MemberName}, Method type: {staticDelegateCacheContext.Method.ContainingType.Name}");
        }
    }
}
