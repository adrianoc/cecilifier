using System.Runtime.InteropServices;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

public class DelayedDefinitionsManager
{
    /// <summary>
    /// 
    /// </summary>
    private List<TypeDefinitionRecord> _postponedTypeDefinitionDetails = new();
    private List<MethodDefinitionRecord> _postponedMethodDefinitionDetails = new();

    private string? _firstMethodHandleVariable = null;

    internal void RegisterTypeDefinition(string typeVarName, string typeQualifiedName, Action<SystemReflectionMetadataContext, TypeDefinitionRecord> action)
    {
        _postponedTypeDefinitionDetails.Add(new TypeDefinitionRecord(typeQualifiedName, typeVarName)
        {
            DefinitionFunction = action,
            FirstFieldHandle = "MetadataTokens.FieldDefinitionHandle(1)"
        });
    }

    internal void RegisterMethodDefinition(string declaringTypeVarName, Func<SystemReflectionMetadataContext, MethodDefinitionRecord, string> newMethodFunc)
    {
        _postponedMethodDefinitionDetails.Add(new MethodDefinitionRecord(newMethodFunc, declaringTypeVarName));
    }

    internal void ProcessDefinitions(SystemReflectionMetadataContext context)
    {
        //TODO: Handle fields && parameters
        var postponedTypeDefinitions = CollectionsMarshal.AsSpan(_postponedTypeDefinitionDetails);
        foreach (var methodRecord in _postponedMethodDefinitionDetails)
        {
            var methodHandleVariableName = methodRecord.DefinitionFunction(context, methodRecord);
            _firstMethodHandleVariable ??= methodHandleVariableName;

            for (int i = 0; i < postponedTypeDefinitions.Length; i++)
            {
                if (postponedTypeDefinitions[i].TypeVarName == methodRecord.DeclaringTypeVarName)
                {
                    postponedTypeDefinitions[i].FirstMethodHandle = methodHandleVariableName;
                }
            }
            //TODO: Update TypeRecord with method handle var.
        }
        
        _firstMethodHandleVariable ??= "MetadataTokens.MethodDefinitionHandle(1)";
        foreach (var typeRecord in _postponedTypeDefinitionDetails)
        {
            typeRecord.DefinitionFunction(context, typeRecord);
        }
    }
}
