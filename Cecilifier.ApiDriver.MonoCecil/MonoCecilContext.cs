using Cecilifier.Core;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.MonoCecil;

public class MonoCecilContext : CecilifierContextBase, IVisitorContext
{
    public MonoCecilContext(CecilifierOptions options, SemanticModel semanticModel, byte indentation = 3) : base(options, semanticModel, indentation)
    {
        TypeResolver = new MonoCecilTypeResolver(this);
        ApiDriver = new MonoCecilGeneratorDriver();
        
        CecilifiedLineNumber = ApiDriver.PreambleLineCount;
        StartLineNumber = ApiDriver.PreambleLineCount;
        ApiDefinitionsFactory = ApiDriver.CreateDefinitionsFactory();
    }

    public static IVisitorContext CreateContext(CecilifierOptions options, SemanticModel semanticModel) => new MonoCecilContext(options, semanticModel);
}
