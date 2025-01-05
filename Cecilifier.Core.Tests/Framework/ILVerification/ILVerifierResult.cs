using System.Reflection.Metadata;
using Cecilifier.Core.Tests.Framework.Extensions;
using System.Text;
using ILVerify;

namespace Cecilifier.Core.Tests.Framework.ILVerification;

public class ILVerifierResult
{
    public readonly VerificationResult Result;
    public readonly string TypeFullName;
    public readonly string MethodSignature;

    public ILVerifierResult(VerificationResult result, string typeFullName, string methodSignature)
    {
        Result = result;
        TypeFullName = typeFullName;
        MethodSignature = methodSignature;
    }

    public static ILVerifierResult From(VerificationResult r, MetadataReader metadataReader) =>
        new(
            r,
            r.Type.IsNil
                ? r.Method.GetMethodDeclaringTypeFullName(metadataReader)
                : r.Type.GetTypeFullName(metadataReader),
            r.Method.IsNil
                ? string.Empty
                : r.Method.GetMethodSignature(metadataReader));

    public string GetErrorMessage()
    {
        var sb = new StringBuilder();
        if (string.IsNullOrEmpty(MethodSignature))
            sb.Append(TypeFullName);
        else
        {
            sb.Append(TypeFullName);
            sb.Append('.');
            sb.Append(MethodSignature);
        }

        sb.Append($" - {Result.Code}: ");
        sb.Append(Result.Message);
        if (Result.ErrorArguments?.Length > 0)
        {
            sb.Append(" - ");
            foreach (var argument in Result.ErrorArguments)
            {
                sb.Append(FormatArgument(argument));
            }
        }

        return sb.ToString();

        static string FormatArgument(ErrorArgument argument)
        {
            if (argument.Name == "Token" && argument.Value is int token)
                return $" {argument.Name} 0x{token:X8}";
            if (argument.Name == "Offset" && argument.Value is int offset)
                return $" {argument.Name} IL_{offset:X4}";
            return $" {argument.Name} {argument.Value}";
        }
    }
}
