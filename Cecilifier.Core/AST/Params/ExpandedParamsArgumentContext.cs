#nullable enable
using System.Diagnostics;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST.Params;

/// <summary>
/// Handles specifics of expanded params argument passing. You can read more details about <see href="">expanded/non-expanded forms</see>.
///
/// When calling methods with `params` parameters users can either pass 1) an array (non-expanded form) or 2) the actual values (expanded form)
///
/// Examples assuming the method below:
/// <code>
/// void M(params int[] ints) { }
/// </code>
/// 
/// 1) Non-Expanded:  M(new [] { 1, 2, 3});
/// 2) Expanded: M(1, 2, 3);
/// </summary>
internal class ExpandedParamsArgumentContext
{
    public ExpandedParamsArgumentContext(IVisitorContext context, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList, string ilVar)
    {
        Context = context;
        BackingVariableName = Context.AddLocalVariableToCurrentMethod($"{paramsParameter.Name}Params", Context.TypeResolver.Resolve(paramsParameter.Type));
        ElementType = paramsParameter.Type.ElementTypeSymbolOf(); 
        FirstArgumentIndex = paramsParameter.Ordinal;
        ElementCount = argumentList.Arguments.Count - paramsParameter.Ordinal;
        ParentArgumentList = argumentList;
        this.ilVar = ilVar;
        _currentIndex = 0;
        _stelemOpCode = ElementType.StelemOpCode();
    }

    private IVisitorContext Context { get; }
    
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
    private int FirstArgumentIndex { get; }
    
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
    public string BackingVariableName { get; }
    
    /// <summary>
    /// The syntax node containing the arguments used in the invocation.
    /// </summary>
    private ArgumentListSyntax ParentArgumentList { get; }
            
    private readonly string ilVar;
    private int _currentIndex;
    private OpCode _stelemOpCode; 

    /// <summary>
    /// Callback used to pre-process argument handling. It can be used to inject code
    /// before the code generation for each argument.
    /// </summary>
    /// <param name="argument">Argument being processed.</param>
    internal void PreProcessArgument(ArgumentSyntax argument)
    {
        var argumentIndex = ParentArgumentList.Arguments.IndexOf(argument);
        Debug.Assert(argumentIndex >= 0);

        if (argumentIndex < FirstArgumentIndex)
            return;
                
        if (argumentIndex == FirstArgumentIndex)
        {
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, BackingVariableName);
        }
                
        Context.EmitCilInstruction(ilVar, OpCodes.Dup);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, _currentIndex++);
    }

    /// <summary>
    /// Callback used to post-process argument handling. It can be used to inject code
    /// *after* generating code for each attribute.
    /// </summary>
    /// <param name="argument">Argument being processed.</param>
    internal void PostProcessArgument(ArgumentSyntax argument)
    {
        var argumentIndex = ParentArgumentList.Arguments.IndexOf(argument);
        Debug.Assert(argumentIndex >= 0);

        if (argumentIndex < FirstArgumentIndex)
            return;
                
        Context.EmitCilInstruction(ilVar, _stelemOpCode);
    }
}
