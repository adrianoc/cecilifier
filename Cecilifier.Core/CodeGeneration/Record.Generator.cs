using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.CodeGeneration;

public class RecordGenerator
{
    internal static void AddSyntheticMembers(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax record)
    {
        PrimaryConstructorGenerator.AddPropertiesFrom(context, recordTypeDefinitionVariable, record);
        PrimaryConstructorGenerator.AddPrimaryConstructor(context, recordTypeDefinitionVariable, record);
    }
}
