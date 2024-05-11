using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.CodeGeneration.Extensions;

public static class SyntaxNodeExtensions
{
    /// <summary>
    /// Each primary constructor parameter that does not have a matching parameter
    /// in the base record (or the base record of the base record and so on) will have
    /// a property associated with it.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="context"></param>
    /// <returns>Returns a list of parameters that does not exist in the base type of the <paramref name="type"/> or in its parents.</returns>
    internal static IReadOnlyList<ParameterSyntax> GetUniqueParameters(this TypeDeclarationSyntax type, IVisitorContext context)
    {
        var records = context.SemanticModel.SyntaxTree.GetRoot().DescendantNodesAndSelf().OfType<RecordDeclarationSyntax>().ToArray();
        List<ParameterSyntax> basesParameters = new();
        var current = type;
        while(true)
        {
            if (current.BaseList?.Types.Count is null or 0)
                break;

            var baseRecordName = current.BaseList!.Types.First().Type.NameFrom();
            var baseRecord = records.Single(r => r.Identifier.ValueText() == baseRecordName);
            basesParameters.AddRange(baseRecord.ParameterList!.Parameters);
            
            current = baseRecord;
        }

        return (IReadOnlyList<ParameterSyntax>) 
               type.ParameterList?.Parameters.Where(candidate => basesParameters.All(bp => candidate.Identifier.ValueText != bp.Identifier.ValueText)).ToList() 
               ?? [];
    }
}
