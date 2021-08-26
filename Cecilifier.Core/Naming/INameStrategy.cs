using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Naming
{
    public interface INameStrategy
    {
        string SyntheticVariable(string name, ElementKind kind);
        string LocalVariable(MemberDeclarationSyntax node, string prefix="");
        string Constructor(BaseTypeDeclarationSyntax declaringType, bool isStatic);
        string Type(MemberDeclarationSyntax node);
        string Type(string baseName, ElementKind elementKind);
        string Parameter(ParameterSyntax parameterSyntax);
        string Parameter(string parameterSyntax, string relatedMember);
        string MemberReference(string baseName, string declaringTypeName);
        string MethodDeclaration(BaseMethodDeclarationSyntax node);
        string GenericParameterDeclaration(TypeParameterSyntax typeParameter);
        string ILProcessor(string memberName, string declaringTypeName);
        string EventDeclaration(MemberDeclarationSyntax eventDeclaration);
        string FieldDeclaration(MemberDeclarationSyntax node, string prefix = "");
        string Label(string name);
        string PropertyDeclaration(BasePropertyDeclarationSyntax propertyDeclarationSyntax);
        string GenericInstance(ISymbol member);
        string Instruction(string toString);
        string CustomAttribute(string typeName);
        string RequiredModifier(MemberDeclarationSyntax memberDeclarationSyntax);
        string Delegate(DelegateDeclarationSyntax node);
        NamingOptions Options { get; set; }
    }
}
