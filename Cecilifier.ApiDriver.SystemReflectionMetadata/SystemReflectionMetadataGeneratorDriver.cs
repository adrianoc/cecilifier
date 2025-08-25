using System.Reflection;
using System.Reflection.Emit;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

public class SystemReflectionMetadataGeneratorDriver : IILGeneratorApiDriver
{
    public string AsCecilApplication(string cecilifiedCode, string mainTypeName, string? entryPointVar)
    {
        var entryPointExpression = entryPointVar ?? "MetadataTokens.MethodDefinitionHandle(0)";
        return $$"""
                 using System;
                 using System.IO;
                 using System.Collections.Immutable;
                 using System.Reflection;
                 using System.Reflection.Metadata;
                 using System.Reflection.Metadata.Ecma335;
                 using System.Reflection.PortableExecutable;

                 public class SnippetRunner
                 {
                    public static void Main(string[] args)
                    {
                        using (var peStream = new FileStream($"{args[0]}", FileMode.Create))
                        {
                            var ilBuilder = new BlobBuilder();
                            var metadataBuilder = new MetadataBuilder();
                            GenerateIL(metadataBuilder, ilBuilder, "{{mainTypeName}}");
                            WritePEImage(peStream, metadataBuilder, ilBuilder, {{entryPointExpression}});
                            peStream.Position = 0;
                        }

                        //Writes a {{Constants.Common.RuntimeConfigJsonExt}} file matching the output assembly name.
                 		File.Copy(
                 				Path.ChangeExtension(typeof(SnippetRunner).Assembly.Location, "{{Constants.Common.RuntimeConfigJsonExt}}"),
                                Path.ChangeExtension(args[0], "{{Constants.Common.RuntimeConfigJsonExt}}"),
                                true);
                    }
                    
                    static void WritePEImage(
                                    Stream peStream,
                                    MetadataBuilder metadataBuilder,
                                    BlobBuilder ilBuilder,
                                    MethodDefinitionHandle entryPointHandle)
                    {
                         var peHeaderBuilder = new PEHeaderBuilder(
                                                    imageCharacteristics: entryPointHandle.IsNil ? Characteristics.Dll : Characteristics.ExecutableImage,
                                                    machine: Machine.Unknown);

                         BlobContentId s_contentId = new BlobContentId(Guid.NewGuid(), 0x04030201);
                         var peBuilder = new ManagedPEBuilder(
                                                peHeaderBuilder,
                                                new MetadataRootBuilder(metadataBuilder),
                                                ilBuilder,
                                                entryPoint: entryPointHandle,
                                                flags: CorFlags.ILOnly,
                                                deterministicIdProvider: content => s_contentId);

                         var peBlob = new BlobBuilder();
                         var contentId = peBuilder.Serialize(peBlob);

                         peBlob.WriteContentTo(peStream);
                    }
                    
                    static void GenerateIL(MetadataBuilder metadata, BlobBuilder ilBuilder, string mainTypeName)
                    {
                         var moduleAndAssemblyName = metadata.GetOrAddString($"{mainTypeName}");
                         var mainModuleHandle = metadata.AddModule(
                             0,
                             moduleAndAssemblyName,
                             metadata.GetOrAddGuid(Guid.NewGuid()),
                             default(GuidHandle),
                             default(GuidHandle));
                     
                         var assemblyRef = metadata.AddAssembly(
                             moduleAndAssemblyName,
                             version: new Version(1, 0, 0, 0),
                             culture: default(StringHandle),
                             publicKey: default,
                             flags: 0,
                             hashAlgorithm: AssemblyHashAlgorithm.None);
                             
                         var mscorlibAssemblyRef = metadata.AddAssemblyReference(
                             name: metadata.GetOrAddString("mscorlib"),
                             version: new Version(4, 0, 0, 0),
                             culture: default(StringHandle),
                             publicKeyOrToken: metadata.GetOrAddBlob(ImmutableArray.Create<byte>(0xB7, 0x7A, 0x5C, 0x56, 0x19, 0x34, 0xE0, 0x89)),
                             flags: default(AssemblyFlags),
                             hashValue: default(BlobHandle));            
                     
                         {{cecilifiedCode}}
                    }
                 }
                 """;
    }

    public int PreambleLineCount => 74;
    
    public IReadOnlyCollection<string> AssemblyReferences { get; } = [typeof(System.Reflection.Metadata.BlobBuilder).Assembly.Location];
    public IApiDriverDefinitionsFactory CreateDefinitionsFactory() => new SystemReflectionMetadataDefinitionsFactory();
    
    public IlContext NewIlContext(IVisitorContext context, string memberName, string methodLocalVar)
    {
        var ilVarName = context.Naming.ILProcessor(memberName);
        context.WriteCecilExpression($"var {ilVarName} = new InstructionEncoder(new BlobBuilder());");
        context.WriteNewLine();
        
        return new SystemReflectionMetadataIlContext(ilVarName);
    }

    public void EmitCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null)
    {
        context.WriteCecilExpression($"{il.VariableName}.OpCode(ILOpCode.{opCode.OpCodeName()});{(comment != null ? $" // {comment}" : string.Empty)}");
        context.WriteNewLine();
        if (operand != null)
        {
            if (operand is CilMetadataHandle handle)
            {
                context.WriteCecilExpression($"{il.VariableName}.Token({handle.VariableName});");
            }
            else
            {
                //TODO: Fix name of WriteX() method to be called; it is not always derivable from the type  
                context.WriteCecilExpression($"{il.VariableName}.CodeBuilder.Write{operand.GetType().Name}({operand});");
            }
            context.WriteNewLine();
        }
    }
    
    public void EmitCilInstruction(IVisitorContext context, IlContext il, OpCode opCode)
    {
        EmitCilInstruction<string>(context, il, opCode, null);
    }
}

public class SystemReflectionMetadataIlContext : IlContext
{
    public SystemReflectionMetadataIlContext(string variableName) : base(variableName)
    {
    }
    
    public int Value { get; set; }
}
