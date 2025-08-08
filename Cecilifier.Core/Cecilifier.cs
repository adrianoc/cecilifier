using System.IO;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core
{
    public sealed class Cecilifier
    {
        private const LanguageVersion CurrentLanguageVersion = LanguageVersion.CSharp13;
        public static readonly int SupportedCSharpVersion = int.Parse(CurrentLanguageVersion.ToString().Substring("CSharp".Length));

        public static CecilifierResult Process(Stream content, CecilifierOptions options)
        {
            InlineArrayGenerator.Reset();
            UsageVisitor.ResetInstance();
            using var stream = new StreamReader(content);
            var syntaxTree = CSharpSyntaxTree.ParseText(stream.ReadToEnd(), new CSharpParseOptions(CurrentLanguageVersion));
            var metadataReferences = options.References.Select(refPath => MetadataReference.CreateFromFile(refPath)).ToArray();

            var comp = CSharpCompilation.Create(
                "CecilifiedAssembly",
                new[] { syntaxTree },
                metadataReferences,
                new CSharpCompilationOptions(OutputKindFor(syntaxTree), allowUnsafe: true));

            var errors = comp.GetDiagnostics()
                                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                                            .Select(CecilifierDiagnostic.FromCompiler)
                                            .ToArray();
            if (errors.Length > 0)
            {
                throw new SyntaxErrorException(errors);
            }

            var semanticModel = comp.GetSemanticModel(syntaxTree);

            var ctx = new CecilifierContext(semanticModel, options);
            var visitor = new CompilationUnitVisitor(ctx);

            syntaxTree.TryGetRoot(out var root);
            visitor.Visit(root);

            var mainTypeName = visitor.MainType != null ? visitor.MainType.Identifier.Text : "Cecilified";
            return new CecilifierResult(new StringReader(options.GeneratorApiDriver.AsCecilApplication(ctx.Output, mainTypeName, visitor.MainMethodDefinitionVariable)), mainTypeName, ctx.Mappings, ctx.Diagnostics);
        }

        private static OutputKind OutputKindFor(SyntaxTree syntaxTree)
        {
            var outputKind = syntaxTree.GetRoot().DescendantNodes().Any(node => node.IsKind(SyntaxKind.GlobalStatement))
                ? OutputKind.ConsoleApplication
                : OutputKind.DynamicallyLinkedLibrary;

            return outputKind;
        }
    }
}
