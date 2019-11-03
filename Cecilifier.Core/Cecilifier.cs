using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            var syntaxTree = CSharpSyntaxTree.ParseText(new StreamReader(content).ReadToEnd(), new CSharpParseOptions(LanguageVersion.CSharp8));
            var comp = CSharpCompilation.Create(
                "CecilifiedAssembly",
                new[] {syntaxTree},
                references.Select(refPath => MetadataReference.CreateFromFile(refPath)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(s => s.ToString()).ToArray();
            if (errors.Length > 0)
            {
                throw new SyntaxErrorException($"Code contains compiler errors:\n\n{string.Join("\n", errors)}");
            }

            var semanticModel = comp.GetSemanticModel(syntaxTree);

            var ctx = new CecilifierContext(semanticModel);
            var visitor = new CompilationUnitVisitor(ctx);

            SyntaxNode root;
            syntaxTree.TryGetRoot(out root);
            visitor.Visit(root);

            //new SyntaxTreeDump("TREE: ", root);

            return new CecilifierResult(new StringReader(ctx.Output.AsCecilApplication()), visitor.MainType != null ? visitor.MainType.Identifier.Text : "Cecilified");
        }

        private SyntaxTree RunTransformations(SyntaxTree tree, SemanticModel semanticModel)
        {
            SyntaxNode root;
            tree.TryGetRoot(out root);

            var cu = (CompilationUnitSyntax) ((CompilationUnitSyntax) root).Accept(new ValueTypeToLocalVariableVisitor(semanticModel));

            return CSharpSyntaxTree.Create(cu);
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
