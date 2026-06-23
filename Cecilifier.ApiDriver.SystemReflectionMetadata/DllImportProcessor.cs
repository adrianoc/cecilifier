using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Cecilifier.Core.Extensions;
using CustomAttributeArgument = Cecilifier.Core.ApiDriver.Attributes.CustomAttributeArgument;
using CustomAttributeNamedArgument = Cecilifier.Core.ApiDriver.Attributes.CustomAttributeNamedArgument;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

public class DllImportProcessor
{
    // Returns the entry point method name in quotes.
    public static string EntryPoint(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments, string originalMethodName) => $"""
                                                                                                 "{AttributePropertyOrDefaultValue(customAttributeArguments, "EntryPoint",$"\"{originalMethodName}\"") }"
                                                                                                 """;
    // For more information and default values see
    // https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dllimportattribute
    public static string MethodImportAttributesFrom(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments)
    {
        return CallingConventionFrom(customAttributeArguments)
            .AppendEnumFlag(CharSetFrom(customAttributeArguments))
            .AppendEnumFlag(SetLastErrorFrom(customAttributeArguments))
            .AppendEnumFlag(ExactSpellingFrom(customAttributeArguments))
            .AppendEnumFlag(BestFitMappingFrom(customAttributeArguments))
            .AppendEnumFlag(ThrowOnUnmappableCharFrom(customAttributeArguments))
            .ToString();
    }    

    static StringBuilder CallingConventionFrom(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments)
    {
        var callConventionSpan = AttributePropertyOrDefaultValue(customAttributeArguments, "CallingConvention", "Winapi").AsSpan();

        // ensures we use the enum member simple name; Parse() fails if we pass a qualified enum member
        var index = callConventionSpan.LastIndexOf('.');
        callConventionSpan = callConventionSpan.Slice(index + 1);

        return new StringBuilder(CallingConventionToCecil(Enum.Parse<CallingConvention>(callConventionSpan)));
    }

    static string CharSetFrom(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments)
    {
        var enumMemberName = AttributePropertyOrDefaultValue(customAttributeArguments, "CharSet", "None").AsSpan();

        // Only use the actual enum member name Parse() fails if we pass a qualified enum member
        var index = enumMemberName.LastIndexOf('.');
        var charSet = Enum.Parse<CharSet>(enumMemberName.Slice(index + 1));
        return charSet == CharSet.None ? string.Empty : $"{AttributeName}.CharSet{charSet}";
    }

    static string SetLastErrorFrom(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments)
    {
        var setLastError = bool.Parse(AttributePropertyOrDefaultValue(customAttributeArguments, "SetLastError", "false"));
        return setLastError ? $"{AttributeName}.SetLastError" : string.Empty;
    }

    static string ExactSpellingFrom(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments)
    {
        var exactSpelling = bool.Parse(AttributePropertyOrDefaultValue(customAttributeArguments, "ExactSpelling", "false"));
        return exactSpelling ? $"{AttributeName}.ExactSpelling" : string.Empty;
    }

    static string BestFitMappingFrom(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments)
    {
        var bestFitMapping = bool.Parse(AttributePropertyOrDefaultValue(customAttributeArguments, "BestFitMapping", "true")) 
            ? "BestFitMappingEnable" 
            : "BestFitMappingDisable";

        return $"{AttributeName}.{bestFitMapping}";
    }

    static string ThrowOnUnmappableCharFrom(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments)
    {
        var bestFitMapping = bool.Parse(AttributePropertyOrDefaultValue(customAttributeArguments, "ThrowOnUnmappableChar", "false")) 
            ? "ThrowOnUnmappableCharEnable" 
            : "ThrowOnUnmappableCharDisable";
        
        return $"{AttributeName}.{bestFitMapping}";
    }

    static string AttributePropertyOrDefaultValue(ReadOnlySpan<CustomAttributeArgument> customAttributeArguments, string propertyName, string defaultValue)
    {
        return customAttributeArguments.ToArray().OfType<CustomAttributeNamedArgument>().FirstOrDefault(arg => arg.Name == propertyName)?.Value?.ToString() ?? defaultValue;
    }

    static string CallingConventionToCecil(CallingConvention callingConvention)
    {
        var callingConventionSuffix = callingConvention switch
        {
            CallingConvention.Cdecl => "CDecl",
            CallingConvention.Winapi => "WinApi",
            CallingConvention.FastCall => "FastCall",
            CallingConvention.StdCall => "StdCall",
            CallingConvention.ThisCall =>  "ThisCall",

            _ => throw new Exception($"Unexpected calling convention: {callingConvention}")
        };

        return $"{AttributeName}.CallingConvention{callingConventionSuffix}";
    }

    private static string AttributeName = nameof(MethodImportAttributes);
}
