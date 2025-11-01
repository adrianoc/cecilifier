using System.Reflection.Emit;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Handles;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
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
        WriteCilInstruction(context, il, branchOpCode, targetLabel, comment);
    }

    public void DefineLabel(IVisitorContext context, IlContext il, string labelVariable)
    {
        context.Generate($"var {labelVariable} = {il.VariableName}.Create(OpCodes.Nop);");
        context.WriteNewLine();
    }

    public void MarkLabel(IVisitorContext context, IlContext il, string labelVariable)
    {
        context.Generate($"{il.VariableName}.Append({labelVariable});");
        context.WriteNewLine();
    }

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
}
