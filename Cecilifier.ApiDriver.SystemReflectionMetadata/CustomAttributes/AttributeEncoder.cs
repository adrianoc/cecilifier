using System.Text;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.CustomAttributes;

internal struct AttributeEncoder
{
    private readonly IVisitorContext _context;
    private readonly string _attributeEncoderVar;
    private readonly string _attributeName;

    public AttributeEncoder(IVisitorContext context, string attributeEncoderVariableName, string attributeName)
    {
        _context = context;
        _attributeName = attributeName;
        _attributeEncoderVar = attributeEncoderVariableName;
    }

    internal string Encode(IList<CustomAttributeArgument> arguments, IList<CustomAttributeNamedArgument> namedArguments)
    {
        var fixedArgumentsEncoderVariableName = _context.Naming.SyntheticVariable($"{_attributeName}ArgumentEncoder", ElementKind.LocalVariable);
        var namedArgumentsEncoderVariableName = _context.Naming.SyntheticVariable($"{_attributeName}NamedArgumentsEncoder", ElementKind.LocalVariable);
        StringBuilder encoded = new($"""
                                     var {_attributeEncoderVar} = new BlobEncoder(new BlobBuilder());
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
            encoded.AppendLine("// Attribute, named arguments");
            encoded.AppendLine($"var nae = {namedArgumentsEncoderVariableName}.Count({namedArguments.Count});");
            foreach (var namedArgument in namedArguments)
            {
                EncodeNamedArgument(namedArgument, encoded, "typeEncoder", "literalEncoder");
            }
        }
    }

    private void EncodeNamedArgument(CustomAttributeNamedArgument namedArgument, StringBuilder encoded, string typeEncoder, string literalEncoder)
    {
        encoded.AppendLine($"// Named argument: {namedArgument.Name}");
        if (namedArgument.Values == null)
            EncodeNamedArgumentScalar(namedArgument, encoded);
        else
            EncodeNamedArgumentArray(namedArgument, encoded);
        encoded.AppendLine();
    }

    private void EncodeNamedArgumentArray(CustomAttributeNamedArgument namedArgument, StringBuilder encoded)
    {
        const string AvoidRedeclarationOfVars = "NamedArgumentEncoderVarsAlreadyDeclared";
        var varOrEmpty = string.Empty;
        if (!_context.HasFlag(AvoidRedeclarationOfVars))
        {
            varOrEmpty = "var ";
            _context.SetFlag(AvoidRedeclarationOfVars);
        }

        encoded.AppendLine($"""nae.AddArgument(isField: {(namedArgument.Kind == NamedArgumentKind.Field).ToKeyword()}, out {varOrEmpty}typeEncoder, out {varOrEmpty}nameEncoder, out {varOrEmpty}literalEncoder);""");
        encoded.AppendLine($"""typeEncoder.{namedArgument.ResolvedType};""");
        encoded.AppendLine($"""nameEncoder.Name("{namedArgument.Name}");""");
        
        var literalsEncoderVariable = _context.Naming.SyntheticVariable("arrayEncoder", ElementKind.LocalVariable);
        encoded.AppendLine($"var {literalsEncoderVariable} = literalEncoder.Vector().Count({namedArgument.Values!.Length});");
        for (int i = 0; i < namedArgument.Values.Length; i++)
        {
            encoded.AppendLine($"{literalsEncoderVariable}.AddLiteral().{ScalarExpressionFor(namedArgument.Values[i])};");
        }
    }

    private void EncodeNamedArgumentScalar(CustomAttributeNamedArgument namedArgument, StringBuilder encoded)
    {
        encoded.AppendLine($"""
                            nae.AddArgument(isField: {(namedArgument.Kind == NamedArgumentKind.Field).ToKeyword()}, 
                                    typeEncoder => typeEncoder.{namedArgument.ResolvedType}, 
                                    nameEncoder => nameEncoder.Name("{namedArgument.Name}"), 
                                    literalEncoder => literalEncoder.{ScalarExpressionFor(namedArgument)});
                            """);
    }
}
