using System.Text;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.CustomAttributes;

internal struct AttributeEncoder
{
    private readonly IVisitorContext _context;
    private string _attributeEncoderVar;
    private readonly string _attributeName;

    public AttributeEncoder(IVisitorContext context, string attributeEncoderVarVariableName, string attributeName)
    {
        _context = context;
        _attributeName = attributeName;
        AttributeBuilderVariable = attributeEncoderVarVariableName;
    }

    private string AttributeBuilderVariable { get; }

    internal string Encode(IList<CustomAttributeArgument> arguments, IList<CustomAttributeNamedArgument> namedArguments)
    {
        _attributeEncoderVar = _context.Naming.SyntheticVariable(_attributeName, ElementKind.Attribute);
        
        var fixedArgumentsEncoderVariableName = _context.Naming.SyntheticVariable("argumentEncoder", ElementKind.LocalVariable);
        var namedArgumentsEncoderVariableName = _context.Naming.SyntheticVariable("namedArgumentsEncoder", ElementKind.LocalVariable);
        StringBuilder encoded = new($"""
                                     var {AttributeBuilderVariable} = new BlobBuilder();
                                     var {_attributeEncoderVar} = new BlobEncoder({AttributeBuilderVariable});

                                     {_attributeEncoderVar}.CustomAttributeSignature(out var {fixedArgumentsEncoderVariableName}, out var {namedArgumentsEncoderVariableName});
                                     """);

        EncodeArguments(fixedArgumentsEncoderVariableName, arguments, encoded);
        EncodeNamedArguments(namedArgumentsEncoderVariableName, namedArguments, encoded);

        return encoded.ToString();
    }

    private void EncodeArguments(string fixedArgumentsEncoderVariableName, IList<CustomAttributeArgument> arguments, StringBuilder encoded)
    {
        foreach (var argument in arguments)
        {
            EncodeArgument(fixedArgumentsEncoderVariableName, argument, encoded);
        }
    }

    private void EncodeArgument(string argumentsEncoderVariableName, CustomAttributeArgument customAttributeArgument, StringBuilder encoded)
    {
        if (customAttributeArgument.Values != null)
            EncodeArray(argumentsEncoderVariableName, encoded, customAttributeArgument);
        else
            EncodeScalar(argumentsEncoderVariableName, encoded, customAttributeArgument);
    }

    private void EncodeScalar(string argumentsEncoderVariableName, StringBuilder encoded, CustomAttributeArgument customAttributeArgument)
    {
        encoded.AppendLine($"""{argumentsEncoderVariableName}.AddArgument().{ScalarExpressionFor(customAttributeArgument)};""");
    }

    private void EncodeArray(string argumentsEncoderVariableName, StringBuilder encoded, CustomAttributeArgument customAttributeArgument)
    {
        encoded.AppendLine($$"""
                             {
                                 var arrayEncoder = {{argumentsEncoderVariableName}}.AddArgument().Vector().Count({{customAttributeArgument.Values!.Length}});
                             """);

        for (int i = 0; i < customAttributeArgument.Values!.Length; i++)
        {
            encoded.AppendLine($"    arrayEncoder.AddLiteral().{ScalarExpressionFor(customAttributeArgument.Values[i])};");
        }

        encoded.AppendLine("}");
    }

    private string ScalarExpressionFor(CustomAttributeArgument customAttributeArgument) => $"Scalar().Constant({customAttributeArgument.Value.ValueText()})";

    private void EncodeNamedArguments(string namedArgumentsEncoderVariableName, IList<CustomAttributeNamedArgument> namedArguments, StringBuilder encoded)
    {
        if (namedArguments.Count == 0)
        {
            encoded.AppendLine($"{_attributeEncoderVar}.Builder.WriteInt16(0); // No named arguments");
        }
        else
        {
            encoded.AppendLine($"var nae = {namedArgumentsEncoderVariableName}.Count({namedArguments.Count});");
            foreach (var namedArgument in namedArguments)
            {
                encoded.AppendLine($"""
                                    nae.AddArgument(
                                                isField: {(namedArgument.Kind == NamedArgumentKind.Field).ToKeyword()}, 
                                                enc => enc.ScalarType().{namedArgument.ResolvedType}, 
                                                nameEncoder => nameEncoder.Name("{namedArgument.Name}"),
                                                literalEncoder => literalEncoder.{ScalarExpressionFor(namedArgument)});
                                    """);
            }
        }
    }
}
