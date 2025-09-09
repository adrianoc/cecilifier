using System.Threading;
using System.Diagnostics;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.Extensions;

namespace Cecilifier.Core.AST;

/// <summary>
/// A `Disposable` type used to emit code to handle passing null as arguments to parameters typed as Nullable{T} 
/// </summary>
/// <remarks>
/// Since Nullable{T} is a struct, passing `null` as an argument the code needs to:
/// 1. Add a synthetic local variable
/// 2. Loads it address to stack
/// 3. Execute InitObj instruction
/// 4. Load the local variable to stack (which will be used as the argument value)
///
/// This type handles 1, 2 & 4.
/// </remarks>
internal ref struct  NullLiteralArgumentDecorator
{
    private string _localVariableName;
    private readonly IVisitorContext _context;
    private readonly string _ilVar;
        
    public NullLiteralArgumentDecorator(IVisitorContext context, ArgumentSyntax node, string ilVar)
    {
        if (node.Expression is not LiteralExpressionSyntax { RawKind: (int) SyntaxKind.NullLiteralExpression })
            return;
            
        var argType = context.SemanticModel.GetTypeInfo(node.Expression).ConvertedType.EnsureNotNull();
        if (!SymbolEqualityComparer.Default.Equals(argType.OriginalDefinition, context.RoslynTypeSystem.SystemNullableOfT))
            return;
            
        // we have a `null` being passed to a Nullable<T> parameter so we need to emit code
        // for steps 1 & 2 as outlined in the remarks section above.
        var local = context.AddLocalVariableToCurrentMethod("tmpNull", context.TypeResolver.ResolveAny(argType));
        context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, local.VariableName);
            
        _localVariableName = local.VariableName;
        _context = context;
        _ilVar = ilVar;
    }

    public void Dispose()
    {
        // Step 4 (see remarks in the method description)
        string localVariable = Interlocked.Exchange(ref _localVariableName, null);
        if (localVariable != null)
        {
            Debug.Assert(_context != null);
            Debug.Assert(_ilVar != null);
            _context!.EmitCilInstruction(_ilVar, OpCodes.Ldloc_S, localVariable);
        }
    }
}
