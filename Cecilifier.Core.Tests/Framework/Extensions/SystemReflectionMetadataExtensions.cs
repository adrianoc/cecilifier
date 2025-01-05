using System.Reflection.Metadata;
using System.Text;
using Cecilifier.Core.Tests.Framework.ILVerification;

namespace Cecilifier.Core.Tests.Framework.Extensions;

public static class Extensions
{
	public static string GetMethodSignature (this MethodDefinitionHandle handle, MetadataReader metadataReader)
	{
		var method = metadataReader.GetMethodDefinition (handle);
        SignatureProvider signatureProvider = new();
		StringBuilder sb = new();
		var signature = method.DecodeSignature(signatureProvider, new object ());
		sb.Append(metadataReader.GetString(method.Name));
		sb.Append ('(');
		int paramIndex = 0;
		foreach (var typeName in signature.ParameterTypes) {
			if (paramIndex > 0)
				sb.Append (',');

			sb.Append (typeName);

			paramIndex++;
		}

		sb.Append (')');
		return sb.ToString ();
	}

	public static string GetMethodDeclaringTypeFullName (this MethodDefinitionHandle handle, MetadataReader metadataReader)
	{
		var definition = metadataReader.GetMethodDefinition (handle);
		var declaringType = definition.GetDeclaringType ();
		return declaringType.GetTypeFullName (metadataReader);
	}

	public static string GetTypeFullName (this TypeDefinitionHandle handle, MetadataReader metadataReader)
	{
		var typeDefinition = metadataReader.GetTypeDefinition (handle);
		var declaringType = typeDefinition.GetDeclaringType ();

		var builder = new StringBuilder ();
		if (!declaringType.IsNil) 
        {
			builder.Append(GetTypeFullName (declaringType, metadataReader))
				.Append ('+')
				.Append (metadataReader.GetString (typeDefinition.Name));
		} else 
        {
			builder.Append(metadataReader.GetString (typeDefinition.Namespace))
				.Append ('.')
				.Append (metadataReader.GetString (typeDefinition.Name));
		}

		return builder.ToString ();
	}

	public static string GetTypeFullName (this TypeReferenceHandle handle, MetadataReader metadataReader)
	{
		var typeReference = metadataReader.GetTypeReference (handle);

		var builder = new StringBuilder ();
		builder.Append (metadataReader.GetString(typeReference.Namespace))
			.Append ('.')
			.Append (metadataReader.GetString(typeReference.Name));

		return builder.ToString ();
	}
}
