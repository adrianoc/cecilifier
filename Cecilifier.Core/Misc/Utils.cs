using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;

#nullable enable
namespace Cecilifier.Core.Misc
{
    public struct Utils
    {
        public static string ConstructorMethodName(bool isStatic) => $".{(isStatic ? Constants.Cecil.StaticConstructorName : Constants.Cecil.InstanceConstructorName)}";

        //TODO: Move to Cecil related code (Cecilifier.ApiDriver.MonoCecil project)
        public static string ImportFromMainModule(string expression) => $"assembly.MainModule.ImportReference({expression})";

        public static string MakeGenericTypeIfAppropriate(IVisitorContext context, ISymbol memberSymbol, string backingFieldVar, string memberDeclaringTypeVar)
        {
            if (!(memberSymbol.ContainingSymbol is INamedTypeSymbol ts) || !ts.IsGenericType || !memberSymbol.IsDefinedInCurrentAssembly(context))
                return backingFieldVar;

            var genTypeVar = context.Naming.GenericInstance(memberSymbol);
            context.Generate($"var {genTypeVar} = {memberDeclaringTypeVar}.MakeGenericInstanceType({memberDeclaringTypeVar}.GenericParameters.ToArray());");
            context.WriteNewLine();

            var fieldRefVar = context.Naming.MemberReference("fld_");
            context.Generate($"var {fieldRefVar} = new FieldReference({backingFieldVar}.Name, {backingFieldVar}.FieldType, {genTypeVar});");
            context.WriteNewLine();

            return fieldRefVar;
        }

        public static T EnsureNotNull<T>([NotNullIfNotNull(nameof(node))] T? node, [CallerArgumentExpression("node")] string? msg = null) where T : SyntaxNode
        {
            return node.EnsureNotNull<T, T>();
        }
        public static string BackingFieldNameForAutoProperty(string propertyName) => $"<{propertyName}>k__BackingField";
    }
}
