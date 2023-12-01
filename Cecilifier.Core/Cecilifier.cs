using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core
{
    public sealed class Cecilifier
    {
        internal const int CecilifierProgramPreambleLength = 25; // The # of lines before the 1st cecilified line of code (see CecilifierExtensions.AsCecilApplication())

        private const LanguageVersion CurrentLanguageVersion = LanguageVersion .CSharp12;
        public static readonly int SupportedCSharpVersion = int.Parse(CurrentLanguageVersion.ToString().Substring("CSharp".Length));

        public static CecilifierResult Process(Stream content, CecilifierOptions options)
        {
            UsageVisitor.ResetInstance();
            using var stream = new StreamReader(content);
            var syntaxTree = CSharpSyntaxTree.ParseText(stream.ReadToEnd(), new CSharpParseOptions(CurrentLanguageVersion));
            var metadataReferences = options.References.Select(refPath => MetadataReference.CreateFromFile(refPath)).ToArray();

            var comp = CSharpCompilation.Create(
                "CecilifiedAssembly",
                new[] { syntaxTree },
                metadataReferences,
                new CSharpCompilationOptions(OutputKindFor(syntaxTree), allowUnsafe: true));

            var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(s => s.ToString()).ToArray();
            if (errors.Length > 0)
            {
                throw new SyntaxErrorException(string.Join("\n", errors));
            }

            var semanticModel = comp.GetSemanticModel(syntaxTree);

            var ctx = new CecilifierContext(semanticModel, options, CecilifierProgramPreambleLength);
            var visitor = new CompilationUnitVisitor(ctx);

            syntaxTree.TryGetRoot(out var root);
            visitor.Visit(root);

            //new SyntaxTreeDump("TREE: ", root);

            var mainTypeName = visitor.MainType != null ? visitor.MainType.Identifier.Text : "Cecilified";
            return new CecilifierResult(new StringReader(ctx.Output.AsCecilApplication(mainTypeName, visitor.MainMethodDefinitionVariable)), mainTypeName, ctx.Mappings);
        }

        private static OutputKind OutputKindFor(SyntaxTree syntaxTree)
        {
            var outputKind = syntaxTree.GetRoot().DescendantNodes().Any(node => node.IsKind(SyntaxKind.GlobalStatement))
                ? OutputKind.ConsoleApplication
                : OutputKind.DynamicallyLinkedLibrary;

            return outputKind;
        }
    }

    public class CecilifierOptions
    {
        public INameStrategy Naming { get; init; } = new DefaultNameStrategy();

        public IReadOnlyList<string> References { get; init; }
    }

    public struct CecilifierResult
    {
        public CecilifierResult(StringReader generatedCode, string mainTypeName, IList<Mapping> mappings)
        {
            GeneratedCode = generatedCode;
            MainTypeName = mainTypeName;
            Mappings = mappings;
        }

        public StringReader GeneratedCode { get; }
        public string MainTypeName { get; }
        public IList<Mapping> Mappings { get; }
    }
}
