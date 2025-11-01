using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

record struct LocalVariableRecord(string VariableName, ResolvedType Type, Action<IVisitorContext, string, ResolvedType> EmitFunction);
