using System;
using System.Collections.Generic;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Naming
{
    public class DefaultNameStrategy : INameStrategy
    {
        private static IReadOnlyDictionary<ElementKind, string> _defaultFormat = new Dictionary<ElementKind, string>
        {
            [ElementKind.Attribute] = "attr",
            [ElementKind.Class] = "cls",
            [ElementKind.Struct] = "st",
            [ElementKind.Interface] = "itf",
            [ElementKind.Enum] = "enum",
            [ElementKind.Delegate] = "del",
            [ElementKind.Method] = "m", 
            [ElementKind.Property] = "prop",
            [ElementKind.Field] = "fld",
            [ElementKind.Event] = "evt",
            [ElementKind.Constructor] = "ctor",
            [ElementKind.StaticConstructor] = "cctor",
            [ElementKind.Label] = "lbl",
            [ElementKind.LocalVariable] = "l",
            [ElementKind.Parameter] = "p",
            [ElementKind.GenericParameter] = "gp",
            [ElementKind.GenericInstance] = "gi",
            [ElementKind.MemberReference] = "r",
            [ElementKind.IL] = "il",
        };

        private IReadOnlyDictionary<ElementKind, string> _format;

        public DefaultNameStrategy()
        {
            Options = NamingOptions.All;
            _format = _defaultFormat;
        }

        public DefaultNameStrategy(NamingOptions namingOptions, IReadOnlyDictionary<ElementKind, string> format)
        {
            _format = format ?? _defaultFormat;
            Options = namingOptions;
        }
        
        public string PartsSeparator { get; } = "_";

        public string SyntheticVariable(string name, ElementKind kind) => $"{PrefixFor(kind)}{NameFor(name)}{UniqueIdString()}";
        public string LocalVariable(MemberDeclarationSyntax node, string prefix = "") => $"{PrefixFor(ElementKind.LocalVariable)}{NameFor(node)}{UniqueIdString()}";
        public string Constructor(BaseTypeDeclarationSyntax declaringType, bool isStatic) => $"{PrefixFor(isStatic ? ElementKind.StaticConstructor : ElementKind.Constructor)}{NameFor(declaringType)}{UniqueIdString()}";
        public string Type(MemberDeclarationSyntax node) => $"{PrefixFor(ElementKindFrom(node))}{NameFor(node)}{UniqueIdString()}";
        public string Type(string baseName, ElementKind elementKind) => $"{PrefixFor(elementKind)}{NameFor(baseName)}{UniqueIdString()}";
        public string Parameter(ParameterSyntax parameterSyntax) => $"{PrefixFor(ElementKind.Parameter)}{NameFor(parameterSyntax.Identifier.Text)}{UniqueIdString()}";
        public string Parameter(string parameterName, string relatedMember) => $"{PrefixFor(ElementKind.Parameter)}{NameFor(parameterName)}{UniqueIdString()}";
        public string MemberReference(string baseName, string declaringTypeName) => $"{PrefixFor(ElementKind.MemberReference)}{NameFor(baseName)}{UniqueIdString()}";
        public string MethodDeclaration(BaseMethodDeclarationSyntax node) => $"{PrefixFor(ElementKind.Method)}{NameFor(node)}{UniqueIdString()}";
        public string GenericParameterDeclaration(TypeParameterSyntax typeParameter) => $"{PrefixFor(ElementKind.GenericParameter)}{NameFor(typeParameter.Identifier.Text)}{UniqueIdString()}";
        public string ILProcessor(string memberName, string declaringTypeName) => $"{PrefixFor(ElementKind.IL)}{NameFor(memberName)}{UniqueIdString()}";
        public string EventDeclaration(MemberDeclarationSyntax eventDeclaration) => $"{PrefixFor(ElementKind.Event)}{NameFor(eventDeclaration)}{UniqueIdString()}";
        public string FieldDeclaration(MemberDeclarationSyntax node, string prefix = "") => $"{PrefixFor(ElementKind.Field)}{NameFor(node)}{UniqueIdString()}";
        public string Label(string name) => $"{PrefixFor(ElementKind.Label)}{NameFor(name)}{UniqueIdString()}";
        public string PropertyDeclaration(BasePropertyDeclarationSyntax propertyDeclarationSyntax) => $"{PrefixFor(ElementKind.Property)}{NameFor(propertyDeclarationSyntax)}{UniqueIdString()}";
        public string GenericInstance(ISymbol member) => $"{PrefixFor(ElementKind.GenericInstance)}{NameFor(member)}{UniqueIdString()}";

        public string Instruction(string opCodeName) => $"{ILOpcodeFor(opCodeName)}{UniqueIdString()}";

        public string CustomAttribute(string typeName) => $"{PrefixFor(ElementKind.Attribute)}{NameFor(typeName)}{UniqueIdString()}";
        public string RequiredModifier(MemberDeclarationSyntax member) => $"modReq{UniqueIdString()}";
        public string Delegate(DelegateDeclarationSyntax node) => $"{PrefixFor(ElementKind.Delegate)}{NameFor(node)}{UniqueId()}";

        public NamingOptions Options { get; set; } = NamingOptions.All;

        private string PrefixFor(ElementKind kind) => (Options & NamingOptions.PrefixVariableNamesWithElementKind) == NamingOptions.PrefixVariableNamesWithElementKind ? $"{_format[kind]}" : string.Empty;
        private string UniqueIdString() => (Options & NamingOptions.SuffixVariableNamesWithUniqueId) == NamingOptions.SuffixVariableNamesWithUniqueId ? $"{PartsSeparator}{UniqueId()}": string.Empty;
        private string NameFor(ISymbol member) => NameFor(member.Name);
        private string NameFor(MemberDeclarationSyntax node) => NameFor(node.Name());
        private string NameFor(string name)
        {
            if ((Options & NamingOptions.AppendElementNameToVariables) != NamingOptions.AppendElementNameToVariables)
                return string.Empty;
            
            var casingAdjustedName = (Options & NamingOptions.CamelCaseElementNames) == NamingOptions.CamelCaseElementNames ? name.CamelCase() : name;
            return $"{PartsSeparator}{casingAdjustedName}";
        }
        
        private string ILOpcodeFor(string opCodeName)
        {
            if ((Options & NamingOptions.PrefixInstructionsWithILOpCodeName) != NamingOptions.PrefixInstructionsWithILOpCodeName)
                return "inst";
             
            return (Options & NamingOptions.CamelCaseElementNames) == NamingOptions.CamelCaseElementNames ? opCodeName.CamelCase() : opCodeName;
        }

        private ElementKind ElementKindFrom(MemberDeclarationSyntax node) => node.Kind() switch
        {
            SyntaxKind.ClassDeclaration => ElementKind.Class,
            SyntaxKind.StructDeclaration => ElementKind.Struct,
            SyntaxKind.EnumDeclaration => ElementKind.Enum,
            SyntaxKind.InterfaceDeclaration => ElementKind.Interface,
            SyntaxKind.DelegateDeclaration => ElementKind.Delegate,
            _ => throw new NotSupportedException($"Cannot map {node.Kind()} to ElementKind")
        };

        private int UniqueId() => _id++;
        private int _id;
    }
}
