using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core
{
    public sealed class Cecilifier
    {
        public static CecilifierResult Process(Stream content, IList<string> references)
        {
            var cecilifier = new Cecilifier();
            return cecilifier.Run(content, references);
        }

        private CecilifierResult Run(Stream content, IList<string> references)
        {
            using var stream = new StreamReader(content);
            var syntaxTree = CSharpSyntaxTree.ParseText(stream.ReadToEnd(), new CSharpParseOptions(LanguageVersion.CSharp9));
            var metadataReferences = references.Select(refPath => MetadataReference.CreateFromFile(refPath)).ToArray();

            var comp = CSharpCompilation.Create(
                "CecilifiedAssembly",
                new[] {syntaxTree},
                metadataReferences,
                new CSharpCompilationOptions(OutputKindFor(syntaxTree), allowUnsafe: true));

            var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(s => s.ToString()).ToArray();
            if (errors.Length > 0)
            {
                throw new SyntaxErrorException($"Code contains compiler errors:\n\n{string.Join("\n", errors)}");
            }

            var semanticModel = comp.GetSemanticModel(syntaxTree);

            var ctx = new CecilifierContext(semanticModel);
            var visitor = new CompilationUnitVisitor(ctx);

            syntaxTree.TryGetRoot(out var root);
            visitor.Visit(root);
            
            //new SyntaxTreeDump("TREE: ", root);

            var mainTypeName = visitor.MainType != null ? visitor.MainType.Identifier.Text : "Cecilified";
            return new CecilifierResult(new StringReader(ctx.Output.AsCecilApplication(mainTypeName, visitor.MainMethodDefinitionVariable)), mainTypeName);
        }

        private static OutputKind OutputKindFor(SyntaxTree syntaxTree)
        {
            var outputKind = syntaxTree.GetRoot().DescendantNodes().Any(node => node.IsKind(SyntaxKind.GlobalStatement)) 
                ? OutputKind.ConsoleApplication 
                : OutputKind.DynamicallyLinkedLibrary;

            return outputKind;
        }
    }

    public struct CecilifierResult
    {
        public CecilifierResult(StringReader generatedCode, string mainTypeName)
        {
            GeneratedCode = generatedCode;
            MainTypeName = mainTypeName;
        }

        public StringReader GeneratedCode { get; set; }
        public string MainTypeName { get; set; }
    }
}
