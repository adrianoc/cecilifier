using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

#nullable enable
namespace Cecilifier.Core.Misc
{
    public struct Utils
    {
        public static string ConstructorMethodName(bool isStatic) => $".{(isStatic ? Constants.Cecil.StaticConstructorName : Constants.Cecil.InstanceConstructorName)}";

        //TODO: Move to Cecil related code (Cecilifier.ApiDriver.MonoCecil project)
        public static string ImportFromMainModule(string expression) => $"assembly.MainModule.ImportReference({expression})";

        public static string? MakeGenericTypeIfAppropriate(IVisitorContext context, IEventSymbol memberSymbol, string fieldName)
        {
            if (!(memberSymbol.Type is INamedTypeSymbol ts) || !ts.IsGenericType || !memberSymbol.IsDefinedInCurrentAssembly(context))
                return null;

            var openTypeRef = context.TypeResolver.Resolve(memberSymbol.Type.OriginalDefinition, ResolveTargetKind.LocalVariable);
            var declaringType = context.TypeResolver.Resolve(memberSymbol.ContainingType, ResolveTargetKind.LocalVariable);
            var instantiatedGenericType = context.TypeResolver.MakeGenericInstanceType(openTypeRef, ts, ResolveTargetKind.LocalVariable);
            
            var fieldRefVar = context.Naming.MemberReference("fld_");
            var fieldRefStatements = context.ApiDefinitionsFactory.FieldReference(context, fieldRefVar, fieldName, instantiatedGenericType, declaringType);
            context.Generate(fieldRefStatements);

            return fieldRefVar;
        }

        public static T EnsureNotNull<T>([NotNullIfNotNull(nameof(node))] T? node, [CallerArgumentExpression("node")] string? msg = null) where T : SyntaxNode
        {
            return node.EnsureNotNull<T, T>();
        }
        public static string BackingFieldNameForAutoProperty(string propertyName) => $"<{propertyName}>k__BackingField";
    }
}
