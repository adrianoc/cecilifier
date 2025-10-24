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

    public AttributeEncoder(IVisitorContext context, string attributeEncoderVarVariableName)
    {
        _context = context;
        AttributeBuilderVariable = attributeEncoderVarVariableName;
    }

    public string AttributeBuilderVariable { get; }

    internal string Encode(IList<CustomAttributeArgument> arguments, IList<CustomAttributeNamedArgument> namedArguments)
    {
        _attributeEncoderVar = _context.Naming.SyntheticVariable("attribute", ElementKind.Attribute);

        StringBuilder encoded = new($"""
                                     var {AttributeBuilderVariable} = new BlobBuilder();
                                     var {_attributeEncoderVar} = new BlobEncoder({AttributeBuilderVariable});

                                     {_attributeEncoderVar}.CustomAttributeSignature(out var fixedArgumentsEncoder, out var namedArgumentsEncoder);
                                     """);

        EncodeArguments(arguments, encoded);
        EncodeNamedArguments(namedArguments, encoded);

        return encoded.ToString();
    }

    private void EncodeArguments(IList<CustomAttributeArgument> arguments, StringBuilder encoded)
    {
        foreach (var argument in arguments)
        {
            EncodeArgument(argument, encoded);
        }
    }

    private void EncodeArgument(CustomAttributeArgument customAttributeArgument, StringBuilder encoded)
    {
        if (customAttributeArgument.Values != null)
            EncodeArray(encoded, customAttributeArgument);
        else
            EncodeScalar(encoded, customAttributeArgument);
    }

    private void EncodeScalar(StringBuilder encoded, CustomAttributeArgument customAttributeArgument)
    {
        encoded.AppendLine($"""fixedArgumentsEncoder.AddArgument().{ScalarExpressionFor(customAttributeArgument)};""");
    }

    private void EncodeArray(StringBuilder encoded, CustomAttributeArgument customAttributeArgument)
    {
        encoded.AppendLine($$"""
                             {
                                 var arrayEncoder = fixedArgumentsEncoder.AddArgument().Vector().Count({{customAttributeArgument.Values!.Length}});
                             """);

        for (int i = 0; i < customAttributeArgument.Values!.Length; i++)
        {
            encoded.AppendLine($"    arrayEncoder.AddLiteral().{ScalarExpressionFor(customAttributeArgument.Values[i])};");
        }

        encoded.AppendLine("}");
    }

    private string ScalarExpressionFor(CustomAttributeArgument customAttributeArgument) => $"Scalar().Constant({customAttributeArgument.Value.ValueText()})";

    private void EncodeNamedArguments(IList<CustomAttributeNamedArgument> namedArguments, StringBuilder encoded)
    {
        if (namedArguments.Count == 0)
        {
            encoded.AppendLine($"{_attributeEncoderVar}.Builder.WriteInt16(0); // No named arguments");
        }
        else
        {
            encoded.AppendLine($"var nae = namedArgumentsEncoder.Count({namedArguments.Count});");
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
