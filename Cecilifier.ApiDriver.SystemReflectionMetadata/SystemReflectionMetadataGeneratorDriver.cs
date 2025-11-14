using System.Reflection.Emit;
using Cecilifier.Core;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.DefinitionsFactory;
using Cecilifier.Core.ApiDriver.Handles;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;

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
                            var mappedFieldData = new BlobBuilder();
                            var metadataBuilder = new MetadataBuilder();
                            var entryPointOrNull = GenerateIL(metadataBuilder, ilBuilder, "{{mainTypeName}}", mappedFieldData);
                            WritePEImage(peStream, metadataBuilder, ilBuilder, entryPointOrNull, mappedFieldData);
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
                                    MethodDefinitionHandle entryPointHandle,
                                    BlobBuilder mappedFieldData)
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
                                                deterministicIdProvider: content => s_contentId,
                                                mappedFieldData: mappedFieldData);

                         var peBlob = new BlobBuilder();
                         var contentId = peBuilder.Serialize(peBlob);

                         peBlob.WriteContentTo(peStream);
                    }
                    
                    static MethodDefinitionHandle GenerateIL(MetadataBuilder metadata, BlobBuilder ilBuilder, string mainTypeName, BlobBuilder mappedFieldData)
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
                             
                         metadata.AddTypeDefinition(
                            default(TypeAttributes),
                            default(StringHandle),
                            metadata.GetOrAddString("<Module>"),
                            baseType: default(EntityHandle),
                            fieldList: MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1),
                            methodList: MetadataTokens.MethodDefinitionHandle(metadata.GetRowCount(TableIndex.MethodDef) + 1));
                            
                         var methodBodyStream = new MethodBodyStreamEncoder(ilBuilder);
                         
                 {{cecilifiedCode}}
                         
                         return {{entryPointExpression}};
                    }
                    
                    static int TokenForType(Action<SignatureTypeEncoder> encode, MetadataBuilder metadata)
                 	{
                 	    var signatureEncoder = new SignatureTypeEncoder(new BlobBuilder());
                        encode(signatureEncoder);
                        return MetadataTokens.GetToken(metadata.AddTypeSpecification(metadata.GetOrAddBlob(signatureEncoder.Builder)));
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
        context.Generate($"var {ilVarName} = new InstructionEncoder(new BlobBuilder(), new ControlFlowBuilder());");
        context.WriteNewLine();
        return new SystemReflectionMetadataIlContext(ilVarName, relatedMethodVar);
    }

    public string EmitCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null)
    {
        var mappedOpCodeName = MapSystemReflectionOpCodeNameToSystemReflectionMetadata(opCode);
        var emitted = $"{il.VariableName}.OpCode(ILOpCode.{mappedOpCodeName});{(comment != null ? $" // {comment}" : string.Empty)}";
        if (operand == null || operand is ResolvedType { Expression: null })
            return emitted;

        return $$""""
            {{emitted}}
            {{
                operand switch
                {
                    string => $"{il.VariableName}.Token(MetadataTokens.GetToken(metadata.GetOrAddUserString({operand})));",
                    CilToken handle => $"{il.VariableName}.Token({handle.VariableName});",
                    // Even though the real operand type may be `Boolean` the operand value and the opCode emitted are for Int32 (that is because IL handles bools as ints)  
                    CilOperandValue { Type.SpecialType: SpecialType.System_Boolean } operandValue => $"{il.VariableName}.CodeBuilder.WriteInt32({operandValue.Value});",
                    CilOperandValue { Type.SpecialType: SpecialType.System_Char } operandValue => $"{il.VariableName}.CodeBuilder.WriteInt32({operandValue.Value});",
                    CilOperandValue { Type.TypeKind: TypeKind.Enum } enumValue => $"{il.VariableName}.CodeBuilder.Write{((INamedTypeSymbol) enumValue.Type).EnumUnderlyingType!.Name}({(int)enumValue.Value});",
                    CilOperandValue operandValue => $"{il.VariableName}.CodeBuilder.Write{operandValue.Type.Name}({operandValue.Value});",
                    CilLocalVariableHandle localVariableHandle => $"{il.VariableName}.CodeBuilder.WriteInt32({localVariableHandle.Value});",

                    //TODO: Fix name of WriteX() method to be called; it is not always derivable from the type  
                    _ => $"{il.VariableName}.CodeBuilder.Write{operand.GetType().Name}({operand});"
                }            
            }}
            """";
    }

    public void WriteCilInstruction<T>(IVisitorContext context, IlContext il, OpCode opCode, T? operand, string? comment = null)
    {
        context.Generate($"{EmitCilInstruction(context, il, opCode, operand, comment)}"); // Use interpolated string to force usage of CecilifierInterpolatedStringHandler
        context.WriteNewLine();
    }
    
    public void WriteCilInstruction(IVisitorContext context, IlContext il, OpCode opCode)
    {
        WriteCilInstruction<string>(context, il, opCode, null);
    }

    public void WriteCilBranch(IVisitorContext context, IlContext il, OpCode branchOpCode, string targetLabel, string? comment = null)
    {
        context.Generate($"{il.VariableName}.Branch(ILOpCode.{branchOpCode.OpCodeName()}, {targetLabel});");
        context.WriteNewLine();
    }

    public void DefineLabel(IVisitorContext context, IlContext il, string labelVariable)
    {
        context.Generate($"var {labelVariable} = {il.VariableName}.DefineLabel();");
        context.WriteNewLine();
    }

    public void MarkLabel(IVisitorContext context, IlContext il, string labelVariable)
    {
        context.Generate($"{il.VariableName}.MarkLabel({labelVariable});");
        context.WriteNewLine();
    }

    public void AddMethodSemantics(IVisitorContext context, string targetVariable, string methodVariable, MethodKind methodKind)
    {
        // In SRM, properties/event methods are handled in IApiDriverDefinitionsFactory.Property(). 
    }

    /// <summary>
    /// Maps Ldc_Ix => Ldc_ix, Ldc_Rx, Conv_Ix => Ldc_rx, etc. 
    /// </summary>
    private static string MapSystemReflectionOpCodeNameToSystemReflectionMetadata(OpCode opCode)
    {
        var reflectionOpCodeName = opCode.OpCodeName();
        var index = reflectionOpCodeName.IndexOf('_');
        if (index > -1)
        {
            Span<char> span = stackalloc char[reflectionOpCodeName.Length];
            reflectionOpCodeName.AsSpan().CopyTo(span);
            var toConvertToLower = span.Slice(index + 1);
            for (int i = 0; i < toConvertToLower.Length; i++)
            {
                toConvertToLower[i] = char.ToLowerInvariant(toConvertToLower[i]);
            }

            return span.ToString();
        }
        return reflectionOpCodeName;
    }
}
