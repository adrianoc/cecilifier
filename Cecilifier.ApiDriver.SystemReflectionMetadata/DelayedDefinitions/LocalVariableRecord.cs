using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

record struct LocalVariableRecord(string VariableName, string Type, Action<IVisitorContext, string, string> EmitFunction);
