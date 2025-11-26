using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cecilifier.Core.AST;
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
/// <remarks>
/// 
/// </remarks>
public class DelayedDefinitionsManager
{
    private readonly Dictionary<string, TypeDefinitionRecord> _postponedTypeDefinitions = new();
    private readonly List<string> _typeDefinitionOrder = new();

    private MethodDefinitionRecord _currentMethod;

    internal void RegisterTypeDefinition(string typeVarName, string typeQualifiedName, DelayedTypeDefinitionAction action)
    {
        _postponedTypeDefinitions.Add(typeVarName, new TypeDefinitionRecord(typeQualifiedName, typeVarName)
        {
            DefinitionFunction = action,
            FirstFieldHandle = null,
            FirstMethodHandle = null
        });
        
        _typeDefinitionOrder.Add(typeVarName);
    }

    internal void RegisterMethodDefinition(string declaringTypeVarName, Func<SystemReflectionMetadataContext, MethodDefinitionRecord, string> newMethodFunc)
    {
        ref var declaringTypeRecord = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, declaringTypeVarName);
        Debug.Assert(!Unsafe.IsNullRef(ref declaringTypeRecord));
        
        declaringTypeRecord.Methods.Add(_currentMethod = new MethodDefinitionRecord(newMethodFunc, declaringTypeVarName));
    }
    
    internal void RegisterFieldDefinition(string declaringTypeVarName, string fieldVariableName)
    {
        ref var typeRecordOrNullRef = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, declaringTypeVarName);
        Debug.Assert(!Unsafe.IsNullRef(ref typeRecordOrNullRef));
        
        typeRecordOrNullRef.FirstFieldHandle ??= fieldVariableName;
    }
    
    internal void RegisterFieldDefinition(string declaringTypeVarName, Func<int, string?> fieldDefinitionFunction)
    {
        ref var typeRecordOrNullRef = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, declaringTypeVarName);
        Debug.Assert(!Unsafe.IsNullRef(ref typeRecordOrNullRef), $"The type '{declaringTypeVarName}' has not been registered yet.");
        
        typeRecordOrNullRef.Fields.Add(fieldDefinitionFunction);
    }

    public int RegisterLocalVariable(string localVarName, ResolvedType resolvedVarType, Action<IVisitorContext, string, ResolvedType> action)
    {
        Debug.Assert(_currentMethod != null);
        var localVariable = new LocalVariableRecord(localVarName, resolvedVarType, action);

        _currentMethod.LocalVariables.Add(localVariable);
        return _currentMethod.LocalVariables.Count - 1;
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
        var associatedRecord = _postponedTypeDefinitions.Single(rec => rec.Value.TypeReferenceVariable == typeReferenceVariable).Value;
        return string.IsNullOrWhiteSpace(associatedRecord.TypeDefinitionVariable) 
            ? throw new InvalidOperationException($"The 'type definition variable' for the type reference '{typeReferenceVariable}' has not been set.") 
            : associatedRecord.TypeDefinitionVariable;
    }

    internal void ProcessDefinitions(SystemReflectionMetadataContext context)
    {
        if (_postponedTypeDefinitions.Count == 0)
            return;
        
        ProcessMethodRecords(context);
        EnsureTypeDefinitionRecordsHaveFirstHandlesInitialized();

        foreach (var typeVarName in _typeDefinitionOrder)
        {
            ref var typeRecord = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, typeVarName);
            Debug.Assert(!Unsafe.IsNullRef(ref typeRecord));
            
            typeRecord.DefinitionFunction(context, ref typeRecord);
        }
        _postponedTypeDefinitions.Clear();
        _typeDefinitionOrder.Clear();
    }
    
    private void ProcessMethodRecords(SystemReflectionMetadataContext context)
    {
        foreach (var typeDeclarationVarName in _typeDefinitionOrder)
        {
            ref var typeRecordOrNullRef = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, typeDeclarationVarName);
            Debug.Assert(!Unsafe.IsNullRef(ref typeRecordOrNullRef));
            
            foreach (var methodRecord in typeRecordOrNullRef.Methods)
            {
                var methodHandleVariableName = methodRecord.DefinitionFunction(context, methodRecord);
                typeRecordOrNullRef.FirstMethodHandle ??= methodHandleVariableName;
            }
        }
    }

    private void EnsureTypeDefinitionRecordsHaveFirstHandlesInitialized()
    {
        foreach (var typeDeclarationVarName in _typeDefinitionOrder)
        {
            ref var typeRecord = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, typeDeclarationVarName);
            Debug.Assert(!Unsafe.IsNullRef(ref typeRecord));
            
            typeRecord.FirstMethodHandle ??= ApiDriverConstants.MethodDefinitionTableNextAvailableEntry;
            typeRecord.FirstFieldHandle ??= ApiDriverConstants.FieldDefinitionTableNextAvailableEntry;
        }
    }
    
    private ref TypeDefinitionRecord GetCurrentTypeDefinition()
    {
        // This assumes that the last type definition is the current one. This may not hold true if we need to process types in a mixed order (e.g. start processing A then
        // need to emit B and when finishing processing B we get back to process A. After getting back to A this method will incorrectly return B as the current type.
        // We may need to revisit this and track the current type in a stack (and push/pop accordingly when visiting/processing types).
        Debug.Assert(_typeDefinitionOrder.Count > 0);
        var currentTypeVarName = _typeDefinitionOrder[^1];

        ref var valueRefOrNullRef = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, currentTypeVarName);
        Debug.Assert(!Unsafe.IsNullRef(ref valueRefOrNullRef));
        
        return ref valueRefOrNullRef;
    }
}
