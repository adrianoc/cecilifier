using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

internal record struct PropertyDefinitionRecord(string Name, string DefinitionVariable, string DeclaringTypeName, Action<IVisitorContext, string, string, string, string> Processor);
