using System.Diagnostics;
using System.Runtime.InteropServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

//TODO: Handle parameters
/// <summary>
/// System.Reflection.Metadata model requires members (methods, field) as well as parameters
/// to be defined before the containing member (type, method, property) whereas Cecilifier
/// follows Roslyn model, in which type definitions are processed first, followed by its
/// member definitions.
///
/// Upon visiting types/members, SRM driver adds a type/member *reference* and postpones
/// the related type/member *definition* when finishing visiting types. 
/// </summary>
public class DelayedDefinitionsManager
{
    private readonly List<TypeDefinitionRecord> _postponedTypeDefinitionDetails = new();
    private readonly List<MethodDefinitionRecord> _postponedMethodDefinitionDetails = new();
    private readonly Dictionary<string, string> _firstFieldByTypeVariable = new();

    private string? _firstMethodHandleVariable;
    private string? _firstFieldHandleVariable;

    internal void RegisterTypeDefinition(string typeVarName, string typeQualifiedName, Action<SystemReflectionMetadataContext, TypeDefinitionRecord> action)
    {
        _postponedTypeDefinitionDetails.Add(new TypeDefinitionRecord(typeQualifiedName, typeVarName)
        {
            DefinitionFunction = action,
            FirstFieldHandle = null,
            FirstMethodHandle = null
        });
    }

    internal void RegisterMethodDefinition(string declaringTypeVarName, Func<SystemReflectionMetadataContext, MethodDefinitionRecord, string> newMethodFunc)
    {
        _postponedMethodDefinitionDetails.Add(new MethodDefinitionRecord(newMethodFunc, declaringTypeVarName));
    }
    
    internal void RegisterFieldDefinition(string parentDefinitionVariableName, string fieldVariableName)
    {
        _firstFieldHandleVariable ??= fieldVariableName;
        _firstFieldByTypeVariable.TryAdd(parentDefinitionVariableName, fieldVariableName);
    }

    public int RegisterLocalVariable(string localVarName, string resolvedVarType, Action<IVisitorContext, string, string> action)
    {
        var localVariable = new LocalVariableRecord(localVarName, resolvedVarType, action);
        _postponedMethodDefinitionDetails[^1].LocalVariables.Add(localVariable);

        return _postponedMethodDefinitionDetails[^1].LocalVariables.Count - 1;
    }
    
    internal void ProcessDefinitions(SystemReflectionMetadataContext context)
    {
        if (_postponedTypeDefinitionDetails.Count == 0)
            return;
        
        var postponedTypeDefinitions = CollectionsMarshal.AsSpan(_postponedTypeDefinitionDetails);

        ProcessFields(postponedTypeDefinitions);
        ProcessMethodRecords(context, postponedTypeDefinitions);
        UpdateTypeDefinitionRecords(postponedTypeDefinitions);

        foreach (var typeRecord in _postponedTypeDefinitionDetails)
        {
            typeRecord.DefinitionFunction(context, typeRecord);
        }

        _postponedMethodDefinitionDetails.Clear();
        _postponedTypeDefinitionDetails.Clear();
        
        _firstMethodHandleVariable = null;
        _firstFieldHandleVariable = null;
    }

    private void ProcessFields(Span<TypeDefinitionRecord> postponedTypeDefinitions)
    {
        foreach (var (typeVar, fieldVar) in _firstFieldByTypeVariable)
        {
            for (int i = 0; i < postponedTypeDefinitions.Length; i++)
            {
                if (postponedTypeDefinitions[i].TypeVarName == typeVar)
                {
                    Debug.Assert(postponedTypeDefinitions[i].FirstFieldHandle == null);
                    postponedTypeDefinitions[i].FirstFieldHandle = fieldVar;
                }
            }
        }
    }
    
    private void ProcessMethodRecords(SystemReflectionMetadataContext context, Span<TypeDefinitionRecord> postponedTypeDefinitions)
    {
        foreach (var methodRecord in _postponedMethodDefinitionDetails)
        {
            var methodHandleVariableName = methodRecord.DefinitionFunction(context, methodRecord);
            _firstMethodHandleVariable ??= methodHandleVariableName;

            for (int i = 0; i < postponedTypeDefinitions.Length; i++)
            {
                if (postponedTypeDefinitions[i].TypeVarName == methodRecord.DeclaringTypeVarName && postponedTypeDefinitions[i].FirstMethodHandle == null)
                {
                    postponedTypeDefinitions[i].FirstMethodHandle = methodHandleVariableName;
                }
            }
        }
    }

    private void UpdateTypeDefinitionRecords(Span<TypeDefinitionRecord> postponedTypeDefinitions)
    {
        _firstMethodHandleVariable ??= "MetadataTokens.MethodDefinitionHandle(1)";
        _firstFieldHandleVariable ??= "MetadataTokens.FieldDefinitionHandle(1)";
        
        postponedTypeDefinitions[^1].FirstMethodHandle ??= _firstMethodHandleVariable;
        postponedTypeDefinitions[^1].FirstFieldHandle ??= _firstFieldHandleVariable;
        
        for (var index = _postponedTypeDefinitionDetails.Count - 2; index >= 0; index--)
        {
            ref var typeRecord = ref postponedTypeDefinitions[index];
            typeRecord.FirstMethodHandle ??= postponedTypeDefinitions[index + 1].FirstMethodHandle;
            typeRecord.FirstFieldHandle ??= postponedTypeDefinitions[index + 1].FirstFieldHandle;
        }
    }
}
