using System.Diagnostics.CodeAnalysis;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;

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
            var genTypeVar = $"gt_{memberSymbol.Name}_{context.NextLocalVariableTypeId()}";
            context.WriteCecilExpression($"var {genTypeVar} = {memberDeclaringTypeVar}.MakeGenericInstanceType({memberDeclaringTypeVar}.GenericParameters.ToArray());");
            context.WriteNewLine();
            context.WriteCecilExpression($"var {genTypeVar}_ = new FieldReference({backingFieldVar}.Name, {backingFieldVar}.FieldType, {genTypeVar});");
            context.WriteNewLine();

            return $"{genTypeVar}_";
        }
        
        public static void EnsureNotNull([DoesNotReturnIf(true)] bool isNull, string msg)
        {
            if (isNull)
                throw new System.NotSupportedException(msg);
        }
    }
}
