using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

internal record FieldDefinitionRecord(Func<FieldDefinitionRecord, string?> DefinitionFunction)
{
    public IList<Action<IVisitorContext, string>> Attributes { get; } = new List<Action<IVisitorContext, string>>();
    
    public int Index { get; set; }
    
    public static implicit operator FieldDefinitionRecord(Func<FieldDefinitionRecord, string?> definitionFunction) => new(definitionFunction);
}
