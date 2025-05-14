#nullable enable
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST.Params;

/// <summary>
/// Handles specifics of expanded params argument passing. You can read more details about <see href="https://github.com/dotnet/csharpstandard/blob/draft-v8/standard/expressions.md#12642-applicable-function-member">expanded/non-expanded forms</see>.
///
/// When calling methods with `params` parameters users can either pass 1) an array (non-expanded form) or 2) the actual values (expanded form)
///
/// Examples assuming the method below:
/// <code>
/// void M(params int[] ints) { }
/// </code>
/// 
/// 1) Non-Expanded:  M(new [] { 1, 2, 3});
/// 2) Expanded: M(4 5, 6);
///
/// <remarks>
/// When invoked with the expanded form Cecilifier needs to create a variable to hold the argument values. In the example above a new array of
/// length 3 need to be allocated and initialized with values 4, 5 and 6.
/// </remarks>
/// </summary>
internal abstract class ExpandedParamsArgumentHandler
{
    protected readonly string ilVar;
    protected int _currentIndex;

    protected ExpandedParamsArgumentHandler(IVisitorContext context, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList, string ilVar)
    {
        Context = context;
        ElementType = paramsParameter.Type.ElementTypeSymbolOf(); 
        FirstArgumentIndex = paramsParameter.Ordinal;
        ElementCount = argumentList.Arguments.Count - paramsParameter.Ordinal;
        ParentArgumentList = argumentList;
        this.ilVar = ilVar;
        _currentIndex = 0;
        //_stelemOpCode = ElementType.StelemOpCode();
    }

    protected IVisitorContext Context { get; }

    /// <summary>
    /// The index of the first argument in an invocation that is part os the list of values included in the `params`.
    /// For example, in the following code:
    /// <code>
    /// void M(string s, params int[] ints) {}
    ///
    /// M("Test",  42, 1);
    /// </code>
    /// the first argument in the invocation of method `M` that is part of `ints` params is the number 42 which has index 1,
    /// so in this case <see cref="FirstArgumentIndex"/> is set to `1` 
    /// </summary>
    protected int FirstArgumentIndex { get; }

    /// <summary>
    /// The syntax node containing the arguments used in the invocation.
    /// </summary>
    protected ArgumentListSyntax ParentArgumentList { get; }

    /// <summary>
    /// Number of elements in the expanded form that is part of the `params` parameter. Given the following code:
    /// <code>
    /// void M(string s, params int[] ints) {}
    ///
    /// M("Test",  42, 1, 10);
    /// </code>
    /// <see cref="ElementCount"/> will be set to 3.
    /// </summary>
    public int ElementCount { get; }

    public ITypeSymbol ElementType { get; }

    /// <summary>
    /// Callback used to pre-process argument handling. It can be used to inject code
    /// before the code generation for each argument.
    /// </summary>
    /// <param name="argument">Argument being processed.</param>
    internal abstract void PreProcessArgument(ArgumentSyntax argument);

    /// <summary>
    /// Callback used to post-process argument handling. It can be used to inject code
    /// *after* generating code that process each argument in the expanded params items.
    /// </summary>
    /// <param name="argument">Argument being processed.</param>
    internal abstract void PostProcessArgument(ArgumentSyntax argument);

    public virtual void PostProcessArgumentList(ArgumentListSyntax argumentList) { }
}
