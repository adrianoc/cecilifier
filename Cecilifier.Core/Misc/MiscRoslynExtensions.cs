using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core.Misc;

public static class MiscRoslynExtensions
{
    public static string AsParameterAttribute(this RefKind refKind) => refKind switch
    {
        RefKind.Out => Constants.ParameterAttributes.Out,
        RefKind.In => Constants.ParameterAttributes.In,
        _ => string.Empty,
    };

    public static SyntaxKind MapCompoundAssignment(this SyntaxKind toBeMapped) => toBeMapped switch
    {
        SyntaxKind.AddAssignmentExpression => SyntaxKind.PlusToken,
        SyntaxKind.SubtractAssignmentExpression => SyntaxKind.MinusToken,
        SyntaxKind.MultiplyAssignmentExpression  => SyntaxKind.AsteriskToken,
        SyntaxKind.DivideAssignmentExpression  => SyntaxKind.SlashToken,
        _ => SyntaxKind.None
    };
}
