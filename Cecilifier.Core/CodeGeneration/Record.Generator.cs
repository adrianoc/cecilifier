using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.CodeGeneration;

public class RecordGenerator
{
    private string _equalityContractGetMethodVar;
    
    internal void AddSyntheticMembers(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        AddEqualityContractPropertyIfNeeded(context, recordTypeDefinitionVariable, record);
        PrimaryConstructorGenerator.AddPropertiesFrom(context, recordTypeDefinitionVariable, record);
        PrimaryConstructorGenerator.AddPrimaryConstructor(context, recordTypeDefinitionVariable, record);
    }
    private void AddEqualityContractPropertyIfNeeded(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        if (record.IsKind(SyntaxKind.RecordStructDeclaration))
            return;

        //var hasBaseRecord = HasBaseRecord(record);

        const string propertyName = "EqualityContract";
        PropertyGenerator propertyGenerator = new(context);

        var equalityContractPropertyVar = context.Naming.SyntheticVariable(propertyName, ElementKind.Property);
        PropertyGenerationData propertyData = new(
            record.Identifier.ValueText,
            recordTypeDefinitionVariable,
            record.TypeParameterList?.Parameters.Count > 0,
            equalityContractPropertyVar,
            propertyName,
            new Dictionary<string, string> { ["get"] = "MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual" },
            false,
            context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemType),
            string.Empty, // used for registering the parameter for setters. In this case, there are none.
            Array.Empty<ParameterSpec>());
        
        var exps = CecilDefinitionsFactory.PropertyDefinition(propertyData.Variable, propertyName, propertyData.ResolvedType);
        context.WriteCecilExpressions(
        [
            ..exps,
            $"{propertyData.DeclaringTypeVariable}.Properties.Add({propertyData.Variable});"
        ]);

        _equalityContractGetMethodVar = context.Naming.SyntheticVariable("EqualityContract_get", ElementKind.Method);
        using var _ = propertyGenerator.AddGetterMethodDeclaration(
                                                                in propertyData, 
                                                                _equalityContractGetMethodVar, 
                                                                false, 
                                                                string.Empty, // used for registering property parameters. In this case, there are none.
                                                                null);
        
        var getterIlVar = context.Naming.ILProcessor("EqualityContract_get");
        context.WriteCecilExpression($"var {getterIlVar} = {_equalityContractGetMethodVar}.Body.GetILProcessor();");
        context.WriteNewLine();
        var getTypeFromHandleSymbol = (IMethodSymbol) context.RoslynTypeSystem.SystemType.GetMembers("GetTypeFromHandle").First();
        var getterBodyExps = CecilDefinitionsFactory.MethodBody2(
                                                            context.Naming,
                                                            _equalityContractGetMethodVar, 
                                                            getterIlVar,
                                                            [
                                                                OpCodes.Ldtoken.WithOperand(recordTypeDefinitionVariable),
                                                                OpCodes.Call.WithOperand(getTypeFromHandleSymbol.MethodResolverExpression(context)),
                                                                OpCodes.Ret
                                                            ]);
        
        context.WriteCecilExpressions(getterBodyExps);
    }

    private static bool HasBaseRecord(TypeDeclarationSyntax record)
    {
        if (record.BaseList?.Types.Count is 0 or null)
            return false;
        
        var baseRecordName = ((IdentifierNameSyntax) record.BaseList!.Types.First().Type).Identifier;
        var found = record.Ancestors().OfType<CompilationUnitSyntax>().Single().DescendantNodes().SingleOrDefault(candidate => 
                                                                                            candidate is RecordDeclarationSyntax candidateBase 
                                                                                            && candidateBase.Identifier.ValueText == baseRecordName.ValueText
                                                                                            && candidateBase.IsKind(SyntaxKind.RecordDeclaration));

        return found != null;
    }
    }
}
