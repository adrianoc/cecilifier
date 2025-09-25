using System.Reflection.Emit;
using System.Text;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

public class SystemReflectionMetadataGeneratorDriver : ILGeneratorApiDriverBase, IILGeneratorApiDriver
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
                            var entryPointOrNull = GenerateIL(metadataBuilder, ilBuilder, "{{mainTypeName}}");
                            WritePEImage(peStream, metadataBuilder, ilBuilder, entryPointOrNull);
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
                    
                    static MethodDefinitionHandle GenerateIL(MetadataBuilder metadata, BlobBuilder ilBuilder, string mainTypeName)
                    {
                         var moduleAndAssemblyName = metadata.GetOrAddString(mainTypeName);
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
                     
                         metadata.AddTypeDefinition(
                            default(TypeAttributes),
                            default(StringHandle),
                            metadata.GetOrAddString("<Module>"),
                            baseType: default(EntityHandle),
                            fieldList: MetadataTokens.FieldDefinitionHandle(1),
                            methodList: MetadataTokens.MethodDefinitionHandle(1));
                            
                            var methodBodyStream = new MethodBodyStreamEncoder(ilBuilder);
                         
                         {{cecilifiedCode}}
                         
                         return {{entryPointExpression}};
                    }
                 }
                 """;
    }

    public int PreambleLineCount => 74;
    
    public IReadOnlyCollection<string> AssemblyReferences { get; } = [typeof(System.Reflection.Metadata.BlobBuilder).Assembly.Location];
    public IApiDriverDefinitionsFactory CreateDefinitionsFactory() => new SystemReflectionMetadataDefinitionsFactory();
    
    public IlContext NewIlContext(IVisitorContext context, string memberName, string relatedMethodVar)
    {
        var ilVarName = context.Naming.ILProcessor(memberName);
        context.Generate($"var {ilVarName} = new InstructionEncoder(new BlobBuilder());");
        context.WriteNewLine();
        
        return new SystemReflectionMetadataIlContext(ilVarName, relatedMethodVar);
    }

    public string EmitCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null)
    {
        var mappedOpCodeName = MapSystemReflectionOpCodeNameToSystemReflectionMetadata(opCode);
        var emitted = $"{il.VariableName}.OpCode(ILOpCode.{mappedOpCodeName});{(comment != null ? $" // {comment}" : string.Empty)}";
        if (operand == null)
            return emitted;

        return $$""""
            {{emitted}}{{Environment.NewLine}}
            {{
                operand switch
                {
                    string => $"{il.VariableName}.Token(MetadataTokens.GetToken(metadata.GetOrAddUserString({operand})));",
                    CilMetadataHandle handle => $"{il.VariableName}.Token({handle.VariableName});",
                    CilOperandValue operandValue => $"{il.VariableName}.CodeBuilder.Write{operandValue.Type.Name}({operandValue.Value});",

                    //TODO: Fix name of WriteX() method to be called; it is not always derivable from the type  
                    _ => $"{il.VariableName}.CodeBuilder.Write{operand.GetType().Name}({operand});"
                }            
            }}
            """";
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
    
    private static string MapSystemReflectionOpCodeNameToSystemReflectionMetadata(OpCode opCode)
    {
        var reflectionOpCodeName = opCode.OpCodeName();
        if (reflectionOpCodeName.StartsWith("Ldc_"))
        {
            StringBuilder buffer = new(reflectionOpCodeName);
            buffer[4] =  Char.ToLower(buffer[4]); // Ldc_Ix => Ldc_ix, Ldc_Rx => Ldc_rx, etc.
            return buffer.ToString();
        }
            
        return reflectionOpCodeName;
    }
}
