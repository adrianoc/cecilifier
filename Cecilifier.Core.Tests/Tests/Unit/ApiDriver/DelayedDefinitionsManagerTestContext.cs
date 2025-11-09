using System.Collections.Generic;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

namespace Cecilifier.Core.Tests.Tests.Unit.ApiDriver;

record DelayedDefinitionsManagerTestContext
{
    public Dictionary<string, TypeDefinitionRecord> Result { get; } = new();

    public void OnTypeRegistration(SystemReflectionMetadataContext context, ref TypeDefinitionRecord typeDefinitionRecord)
    {
        Result[typeDefinitionRecord.TypeReferenceVariable] = typeDefinitionRecord;
    }
}
