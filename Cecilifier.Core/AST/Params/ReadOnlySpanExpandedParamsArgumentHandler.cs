#nullable enable
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST.Params;

internal class ReadOnlySpanExpandedParamsArgumentHandler : SpanExpandedParamsArgumentHandler
{
    public ReadOnlySpanExpandedParamsArgumentHandler(IVisitorContext context, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList, string ilVar) 
        : base(context, paramsParameter, argumentList, ilVar)
    {
    }

    protected override DefinitionVariable GetInlineArrayToSpanResolvedMethod() => PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayAsReadOnlySpanMethod(Context);
}
