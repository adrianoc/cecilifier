using System.Reflection.Emit;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.DefinitionsFactory;
using Cecilifier.Core.ApiDriver.Handles;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilGeneratorDriver : IILGeneratorApiDriver
{
    public string AsCecilApplication(string cecilifiedCode, string mainTypeName, string? entryPointVar)
    {
        var moduleKind = entryPointVar == null ? "ModuleKind.Dll" : "ModuleKind.Console";
        var entryPointStatement = entryPointVar != null ? $"\t\t\tassembly.EntryPoint = {entryPointVar};\n" : string.Empty;

        return $@"using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System; 
using System.IO;
using System.Linq;
using BindingFlags = System.Reflection.BindingFlags;
using Cecilifier.Runtime;
               
public class SnippetRunner
{{
	public static void Main(string[] args)
	{{
        // setup `reflection/metadata importers` to ensure references to System.Private.CoreLib are replaced with references to the correct reference assemblies`.
        var mp = new ModuleParameters
        {{
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==  System.Runtime.InteropServices.Architecture.Arm64 ? TargetArchitecture.ARM64 : TargetArchitecture.AMD64,
            Kind =  {moduleKind},
            MetadataImporterProvider = new SystemPrivateCoreLibFixerMetadataImporterProvider(),
            ReflectionImporterProvider = new SystemPrivateCoreLibFixerReflectionProvider()
        }};

		using(var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(""{mainTypeName}"", Version.Parse(""1.0.0.0"")), Path.GetFileName(args[0]), mp))
        {{
{cecilifiedCode}{entryPointStatement}
		    assembly.Write(args[0]);

            //Writes a {Constants.Common.RuntimeConfigJsonExt} file matching the output assembly name.
			File.Copy(
				Path.ChangeExtension(typeof(SnippetRunner).Assembly.Location, ""{Constants.Common.RuntimeConfigJsonExt}""),
                Path.ChangeExtension(args[0], ""{Constants.Common.RuntimeConfigJsonExt}""),
                true);
        }}
	}}
}}";
    }

    public ApiDriverCapabilities DriverCapabilities => ApiDriverCapabilities.RequiresForwardReferences | ApiDriverCapabilities.RequiresExplicitParameterSyntaxHandling;

    public int PreambleLineCount => 25; // The # of lines before the 1st cecilified line of code (see `cecilifiedCode` parameter from AsCecilApplication())

    public IReadOnlyCollection<string> AssemblyReferences { get; } = 
        [
            typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location,
            typeof(Mono.Cecil.Rocks.ILParser).Assembly.Location,
        ];

    public IApiDriverDefinitionsFactory CreateDefinitionsFactory() => new MonoCecilDefinitionsFactory();


    public string EmitCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null)
    {
        var operandStr = operand switch
        {
            CilOperandValue cilOperand => $", {cilOperand.Value}",
            CilLocalVariableHandle fieldHandle => $", {fieldHandle.Value}",
            ResolvedType rt => rt.Expression == null ? string.Empty : $", {rt.Expression}", 
            _ => operand == null ? string.Empty : $", {operand}"
        };
        
        return $"{il.VariableName}.Emit({opCode.ConstantName()}{operandStr});{(comment != null ? $" // {comment}" : string.Empty)}";
    }

    public void WriteCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null)
    {
        context.Generate(EmitCilInstruction(context, il, opCode, operand, comment));
        context.WriteNewLine();
    }
    
    public void WriteCilInstruction(IVisitorContext context, IlContext il, OpCode opCode)
    {
        WriteCilInstruction<string>(context, il, opCode, null);
    }

    public void WriteCilBranch(IVisitorContext context, IlContext il, OpCode branchOpCode, string targetLabel, string? comment = null)
    {
        context.Generate(EmitCilInstruction(context, il, branchOpCode, targetLabel, comment));
        context.WriteNewLine();
    }
    
    public string EmitCilBranchInstruction(IVisitorContext context, IlContext il, OpCode branchOpCode, string targetLabel, string? comment = null)
    {
        return EmitCilInstruction(context, il, branchOpCode, targetLabel, comment);
    }

    public void DefineLabel(IVisitorContext context, IlContext il, string labelVariable)
    {
        context.Generate($"var {labelVariable} = {il.VariableName}.Create(OpCodes.Nop);");
        context.WriteNewLine();
    }
    
    public string EmitDefineLabel(IVisitorContext context, IlContext il, string labelVariable) => $"var {labelVariable} = {il.VariableName}.Create(OpCodes.Nop);";
    
    public void MarkLabel(IVisitorContext context, IlContext il, string labelVariable)
    {
        context.Generate($"{il.VariableName}.Append({labelVariable});");
        context.WriteNewLine();
    }

    public string EmitMarkLabel(IVisitorContext context, IlContext il, string labelVariable) => $"{il.VariableName}.Append({labelVariable});";

    public IlContext NewIlContext(IVisitorContext context, string memberName, string relatedMethodVar)
    {
        var ilVarName = context.Naming.ILProcessor(memberName);
        return new MonoCecilDeferredIlContext(context, ilVarName, relatedMethodVar);
    }

    public void AddMethodSemantics(IVisitorContext context, string targetVariable, string methodVariable, MethodKind methodKind)
    {
        var accessor = methodKind switch
        {
            MethodKind.PropertyGet => "GetMethod",
            MethodKind.PropertySet => "SetMethod",
            MethodKind.EventAdd => "AddMethod",
            MethodKind.EventRemove => "RemoveMethod",
            
            _ => throw new ArgumentOutOfRangeException(nameof(methodKind), methodKind, "")
        };
        
        context.Generate([
                $"{methodVariable}.Body = new MethodBody({methodVariable});",
                $"{targetVariable}.{accessor} = {methodVariable};" ]);
    }

    public void WriteExceptionHandlers(IVisitorContext context, IlContext ilVar, IEnumerable<ExceptionHandlerEntry> exceptionHandlerTable)
    {
        string methodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        foreach (var handlerEntry in exceptionHandlerTable)
        {
            context.Generate($"{methodVar}.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.{handlerEntry.Kind})");
            context.WriteNewLine();
            context.Generate("{");
            context.WriteNewLine();
            if (handlerEntry.Kind == ExceptionHandlerKind.Catch)
            {
                context.Generate($"    CatchType = {handlerEntry.CatchType},");
                context.WriteNewLine();
            }

            context.Generate($"    TryStart = {handlerEntry.TryStart},");
            context.WriteNewLine();
            context.Generate($"    TryEnd = {handlerEntry.TryEnd},");
            context.WriteNewLine();
            context.Generate($"    HandlerStart = {handlerEntry.HandlerStart},");
            context.WriteNewLine();
            context.Generate($"    HandlerEnd = {handlerEntry.HandlerEnd}");
            context.WriteNewLine();
            context.Generate("});");
            context.WriteNewLine();
        }
    }
}
