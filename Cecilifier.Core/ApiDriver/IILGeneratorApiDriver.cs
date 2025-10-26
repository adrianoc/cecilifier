#nullable enable
using System.Collections.Generic;
using System.Reflection.Emit;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.ApiDriver;


/// <summary>
/// Interface modeling libraries that emit IL the main one being Mono.Cecil (original target of the project). 
/// First attempt to extract a common interface to add support for System.Reflection.Metadata  
/// </summary>
public interface IILGeneratorApiDriver
{
    string AsCecilApplication(string cecilifiedCode, string mainTypeName, string? entryPointVar);
    int PreambleLineCount { get; }
    IReadOnlyCollection<string> AssemblyReferences { get; }
    
    IApiDriverDefinitionsFactory CreateDefinitionsFactory();

    IlContext NewIlContext(IVisitorContext context, string memberName, string relatedMethodVar);
    
    string EmitCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null);
    void WriteCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null);
    void WriteCilInstruction(IVisitorContext context, IlContext il, OpCode opCode);
    void WriteCilBranch(IVisitorContext context, IlContext il, OpCode branchOpCode, string targetLabel, string? comment = null);
    
    void DefineLabel(IVisitorContext context, IlContext il, string labelVariable);
    void MarkLabel(IVisitorContext context, IlContext il, string labelVariable);
    
    void AddMethodSemantics(IVisitorContext context, string targetVariable, string methodVariable, MethodKind methodKind);
}
