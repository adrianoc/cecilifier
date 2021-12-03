using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;

#nullable enable
namespace Cecilifier.Core.Misc
{
    internal struct Utils
    {
        public static string ImportFromMainModule(string expression) => $"assembly.MainModule.ImportReference({expression})";

        public static string MakeGenericTypeIfAppropriate(IVisitorContext context, ISymbol memberSymbol, string backingFieldVar, string memberDeclaringTypeVar)
        {
            if (!(memberSymbol.ContainingSymbol is INamedTypeSymbol ts) || !ts.IsGenericType || !memberSymbol.IsDefinedInCurrentType(context))
                return backingFieldVar;

            //TODO: Register the following variable?
            var genTypeVar = context.Naming.GenericInstance(memberSymbol);
            context.WriteCecilExpression($"var {genTypeVar} = {memberDeclaringTypeVar}.MakeGenericInstanceType({memberDeclaringTypeVar}.GenericParameters.ToArray());");
            context.WriteNewLine();

            var fieldRefVar = context.Naming.MemberReference("fld_", memberSymbol.ContainingType.Name);
            context.WriteCecilExpression($"var {fieldRefVar} = new FieldReference({backingFieldVar}.Name, {backingFieldVar}.FieldType, {genTypeVar});");
            context.WriteNewLine();

            return fieldRefVar;
        }

        public static void EnsureNotNull([NotNull] ISymbol? symbol, string msg)
        {
            if (symbol == null)
                throw new System.NotSupportedException(msg);
        }
        
        [Conditional("DEBUG")]
        public static void EnsureNotNull([NotNull] SyntaxNode? node, string msg)
        {
            if (node == null)
                throw new System.NotSupportedException(msg);
        }
    }
}
