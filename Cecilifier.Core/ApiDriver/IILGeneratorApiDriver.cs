#nullable enable
using System.Collections.Generic;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver.DefinitionsFactory;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.ApiDriver;


/// <summary>
/// Interface modeling libraries that emit IL the main one being Mono.Cecil (original target of the project). 
/// First attempt to extract a common interface to add support for System.Reflection.Metadata  
/// </summary>
public interface IILGeneratorApiDriver
{
    ApiDriverCapabilities DriverCapabilities => ApiDriverCapabilities.None;
    
    string AsCecilApplication(string cecilifiedCode, string mainTypeName, string? entryPointVar);
    int PreambleLineCount { get; }
    IReadOnlyCollection<string> AssemblyReferences { get; }
    
    IApiDriverDefinitionsFactory CreateDefinitionsFactory();

    IlContext NewIlContext(IVisitorContext context, string memberName, string relatedMethodVar);
    
    string EmitCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null);
    void WriteCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null);
    void WriteCilInstruction(IVisitorContext context, IlContext il, OpCode opCode);
    void WriteCilBranch(IVisitorContext context, IlContext il, OpCode branchOpCode, string targetLabel, string? comment = null);
    string EmitCilBranchInstruction(IVisitorContext context, IlContext il, OpCode branchOpCode, string targetLabel, string? comment = null);
    
    /// <summary>
    /// Define a new label in the IL instruction stream that can be used as a target of instructions such as branch ones.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="il"></param>
    /// <param name="labelVariable">Variable name to be used in future references to the label; for instance, when emitting branches targetting the label.</param>
    /// <remarks>Defining a label simply creates a variable suitable to be used as a target of instructions. The actual instruction referenced by the label needs to be marked by calling <see cref="MarkLabel"/>.</remarks>
    void DefineLabel(IVisitorContext context, IlContext il, string labelVariable);
    string EmitDefineLabel(IVisitorContext context, IlContext il, string labelVariable);
    
    void MarkLabel(IVisitorContext context, IlContext il, string labelVariable);
    string EmitMarkLabel(IVisitorContext context, IlContext il, string labelVariable);
    
    void AddMethodSemantics(IVisitorContext context, string targetVariable, string methodVariable, MethodKind methodKind);
    
    void WriteExceptionHandlers(IVisitorContext context, IlContext ilVar, IEnumerable<ExceptionHandlerEntry> exceptionHandlerTable);
}
