using System.Diagnostics;
using System.Runtime.InteropServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

internal delegate void DelayedTypeDefinitionAction(SystemReflectionMetadataContext context, ref TypeDefinitionRecord definitionRecord);
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
    private readonly Stack<int> _currentTypeDefinitionIndex = new();

    internal void RegisterTypeDefinition(string typeVarName, string typeQualifiedName, DelayedTypeDefinitionAction action)
    {
        _postponedTypeDefinitionDetails.Add(new TypeDefinitionRecord(typeQualifiedName, typeVarName)
        {
            DefinitionFunction = action,
            FirstFieldHandle = null,
            FirstMethodHandle = null
        });
        
        _currentTypeDefinitionIndex.Push(_postponedTypeDefinitionDetails.Count - 1);
    }

    internal void RegisterMethodDefinition(string declaringTypeVarName, Func<SystemReflectionMetadataContext, MethodDefinitionRecord, string> newMethodFunc)
    {
        _postponedMethodDefinitionDetails.Add(new MethodDefinitionRecord(newMethodFunc, declaringTypeVarName));
    }
    
    internal void RegisterFieldDefinition(string parentDefinitionVariableName, string fieldVariableName)
    {
        _firstFieldByTypeVariable.TryAdd(parentDefinitionVariableName, fieldVariableName);
    }

    public int RegisterLocalVariable(string localVarName, ResolvedType resolvedVarType, Action<IVisitorContext, string, ResolvedType> action)
    {
        var localVariable = new LocalVariableRecord(localVarName, resolvedVarType, action);

        //TODO: For now assume the last method is the current one. This may not hold true if we need to process method B while processing method A and after
        //      finishing processing B we get back to process the reminder of A. 
        //      We need to track current `method` (pushing when start visiting, popping when finish).
        _postponedMethodDefinitionDetails[^1].LocalVariables.Add(localVariable);
        return _postponedMethodDefinitionDetails[^1].LocalVariables.Count - 1;
    }
    
    public void RegisterProperty(string propertyName,string propertyDefinitionVariable, string declaringTypeName, string declaringTypeVariable, Action<IVisitorContext, string, string, string, string> propertyProcessor)
    {
        GetCurrentTypeDefinition().Properties.Add(new PropertyDefinitionRecord(propertyName, propertyDefinitionVariable, declaringTypeName, propertyProcessor));
    }

    public void AddAttributeToCurrentType(Action<IVisitorContext, string> attributeEmitter)
    {
        GetCurrentTypeDefinition().Attributes.Add(attributeEmitter);
    }

    public string GetTypeDefinitionVariableFromTypeReferenceVariable(string typeReferenceVariable)
    {
        var associatedRecord = _postponedTypeDefinitionDetails.FirstOrDefault(rec => rec.TypeReferenceVariable == typeReferenceVariable);
        if (associatedRecord == default)
            throw new ArgumentException(nameof(typeReferenceVariable));
        
        return string.IsNullOrWhiteSpace(associatedRecord.TypeDefinitionVariable) 
            ? throw new InvalidOperationException($"The 'type definition variable' for the type reference '{typeReferenceVariable}' has not been set.") 
            : associatedRecord.TypeDefinitionVariable;
    }

    internal void ProcessDefinitions(SystemReflectionMetadataContext context)
    {
        if (_postponedTypeDefinitionDetails.Count == 0)
            return;
        
        var postponedTypeDefinitions = CollectionsMarshal.AsSpan(_postponedTypeDefinitionDetails);

        ProcessFields(postponedTypeDefinitions);
        ProcessMethodRecords(context, postponedTypeDefinitions);
        
        UpdateTypeDefinitionRecords(postponedTypeDefinitions);

        for (var index = 0; index < postponedTypeDefinitions.Length; index++)
        {
            ref var typeRecord = ref postponedTypeDefinitions[index];
            typeRecord.DefinitionFunction(context, ref typeRecord);
        }
        
        _postponedMethodDefinitionDetails.Clear();
        _postponedTypeDefinitionDetails.Clear();
        _currentTypeDefinitionIndex.Clear();
    }

    private void ProcessFields(Span<TypeDefinitionRecord> postponedTypeDefinitions)
    {
        foreach (var (typeVar, fieldVar) in _firstFieldByTypeVariable)
        {
            for (int i = 0; i < postponedTypeDefinitions.Length; i++)
            {
                if (postponedTypeDefinitions[i].TypeReferenceVariable == typeVar)
                {
                    Debug.Assert(postponedTypeDefinitions[i].FirstFieldHandle == null);
                    postponedTypeDefinitions[i].FirstFieldHandle = fieldVar;
                    break;
                }
            }
        }
    }
    
    private void ProcessMethodRecords(SystemReflectionMetadataContext context, Span<TypeDefinitionRecord> postponedTypeDefinitions)
    {
        foreach (var methodRecord in _postponedMethodDefinitionDetails)
        {
            var methodHandleVariableName = methodRecord.DefinitionFunction(context, methodRecord);
            for (int i = 0; i < postponedTypeDefinitions.Length; i++)
            {
                if (postponedTypeDefinitions[i].TypeReferenceVariable == methodRecord.DeclaringTypeVarName && postponedTypeDefinitions[i].FirstMethodHandle == null)
                {
                    postponedTypeDefinitions[i].FirstMethodHandle = methodHandleVariableName;
                    break;
                }
            }
        }
    }

    private void UpdateTypeDefinitionRecords(Span<TypeDefinitionRecord> postponedTypeDefinitions)
    {
        const string methodListForTypeWithNoMethods= "MetadataTokens.MethodDefinitionHandle(metadata.GetRowCount(TableIndex.MethodDef) + 1)";
        const string fieldListForTypeWithNoFields = "MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1)";

        for (var index = 0; index < _postponedTypeDefinitionDetails.Count; index++)
        {
            ref var typeRecord = ref postponedTypeDefinitions[index];
            typeRecord.FirstMethodHandle ??= methodListForTypeWithNoMethods;
            typeRecord.FirstFieldHandle ??= fieldListForTypeWithNoFields;
        }
    }
    
    private ref TypeDefinitionRecord GetCurrentTypeDefinition()
    {
        var index = _currentTypeDefinitionIndex.Peek();
        return ref CollectionsMarshal.AsSpan(_postponedTypeDefinitionDetails)[index];
    }
}
