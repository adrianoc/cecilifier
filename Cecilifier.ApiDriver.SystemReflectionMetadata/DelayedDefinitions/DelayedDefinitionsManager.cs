using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

internal delegate void DelayedTypeDefinitionAction(SystemReflectionMetadataContext context, ref TypeDefinitionRecord definitionRecord);

internal record struct TypeParameterDefinitionRecord(IList<TypeParameterSyntax> TypeParameterSyntaxes, IList<string> TypeParameterStrings, string MethodDefinitionVariable);

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
    private readonly List<TypeParameterDefinitionRecord> _typeParameters = new();

    internal void RegisterTypeDefinition(string typeVarName, DelayedTypeDefinitionAction action)
    {
        _postponedTypeDefinitions.Add(typeVarName, new TypeDefinitionRecord(typeVarName)
        {
            DefinitionFunction = action,
            FirstMethodHandle = null,
        });
        
        _typeDefinitionOrder.Add(typeVarName);
    }

    internal void RegisterMethodDefinition(string declaringTypeVarName, string methodVar, Func<SystemReflectionMetadataContext, MethodDefinitionRecord, string> newMethodFunc)
    {
        ref var declaringTypeRecord = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, declaringTypeVarName);
        Debug.Assert(!Unsafe.IsNullRef(ref declaringTypeRecord));
        
        declaringTypeRecord.Methods.Add(new MethodDefinitionRecord(newMethodFunc, methodVar));
    }
    
    internal void RegisterFieldDefinition(string declaringTypeVarName, in FieldDefinitionRecord field)
    {
        ref var typeRecordOrNullRef = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, declaringTypeVarName);
        Debug.Assert(!Unsafe.IsNullRef(ref typeRecordOrNullRef), $"[Field Index: {field.Index}] The type '{declaringTypeVarName}' has not been registered yet.");
        
        typeRecordOrNullRef.Fields.Add(field);
    }

    public int RegisterLocalVariable(string declaringMethodVariable, string localVarName, ResolvedType resolvedVarType, Action<IVisitorContext, string, ResolvedType> action)
    {
        var localVariable = new LocalVariableRecord(localVarName, resolvedVarType, action);

        var method = _postponedTypeDefinitions.Values.SelectMany(ptd => ptd.Methods).Single(pmd => pmd.MethodVariable == declaringMethodVariable);
        Debug.Assert(method != null, $"Method with variable name {declaringMethodVariable} was not found.");

        method.LocalVariables.Add(localVariable);
        return method.LocalVariables.Count - 1;
    }
    
    public void RegisterProperty(string propertyName,string propertyDefinitionVariable, string declaringTypeName, string declaringTypeVariable, Action<IVisitorContext, string, string, string, string> propertyProcessor)
    {
        GetCurrentTypeDefinition().Properties.Add(new PropertyDefinitionRecord(propertyName, propertyDefinitionVariable, declaringTypeName, propertyProcessor));
    }
    
    public void RegisterEvent(string eventName,string propertyDefinitionVariable, string declaringTypeName, Action<IVisitorContext, string, string, string> eventProcessor)
    {
        GetCurrentTypeDefinition().Events.Add(new EventDefinitionRecord(eventName, propertyDefinitionVariable, declaringTypeName, eventProcessor));
    }

    internal void RegisterTypeParameters(IList<TypeParameterSyntax> typeParameters, string methodDefVar)
    {
        _typeParameters.Add(new TypeParameterDefinitionRecord(typeParameters, [], methodDefVar));
    }
    
    internal void RegisterTypeParameters(IList<string> typeParameters, string methodDefVar)
    {
        _typeParameters.Add(new TypeParameterDefinitionRecord([], typeParameters, methodDefVar));
    }

    /*
     * In order to ensure generic type parameter table respects the sorting rules from ECMA XXXX
     * we need to emit the type parameters for methods only after emitting all types whence we postpone it.
     */
    public void EmitTypeParameters(IVisitorContext context)
    {
        foreach (var typeParameter in _typeParameters)
        {
            if (typeParameter.TypeParameterSyntaxes.Count > 0)
            {
                SRMUtils.AddTypeParameters(context, typeParameter.TypeParameterSyntaxes, typeParameter.MethodDefinitionVariable);
            }
            else if(typeParameter.TypeParameterStrings.Count > 0)
            {
                SRMUtils.AddTypeParameters(context, typeParameter.TypeParameterStrings, typeParameter.MethodDefinitionVariable);
            }
        }
    }
    
    /// <summary>
    /// This method must be invoked after the member has been registered with the DelayedDefinitionsManager, in most cases
    /// this means after the equivalent method in <see cref="Cecilifier.Core.ApiDriver.DefinitionsFactory.IApiDriverDefinitionsFactory"/> has been invoked.
    /// </summary>
    public void AddAttributeEmitterToCurrentMember(VariableMemberKind kind, Action<IVisitorContext, string> attributeEmitter)
    {
        ref var currentType = ref GetCurrentTypeDefinition();
        if (kind == VariableMemberKind.Type)
        {
            currentType.Attributes.Add(attributeEmitter);
        }
        else if (kind == VariableMemberKind.Field)
        {
            currentType.Fields[^1].Attributes.Add(attributeEmitter);
        }
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

        foreach (var typeVarName in _typeDefinitionOrder)
        {
            ref var typeRecord = ref CollectionsMarshal.GetValueRefOrNullRef(_postponedTypeDefinitions, typeVarName);
            Debug.Assert(!Unsafe.IsNullRef(ref typeRecord));
            
            foreach (var methodRecord in typeRecord.Methods)
            {
                var methodHandleVariableName = methodRecord.DefinitionFunction(context, methodRecord);
                typeRecord.FirstMethodHandle ??= methodHandleVariableName;
            }
            
            typeRecord.FirstMethodHandle ??= ApiDriverConstants.MethodDefinitionTableNextAvailableEntry;
            typeRecord.DefinitionFunction(context, ref typeRecord);
        }
        _postponedTypeDefinitions.Clear();
        _typeDefinitionOrder.Clear();
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
