using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;

namespace Cecilifier.Core.Extensions;

public static class CecilifierContextExtensions
{
    internal static void AddCompilerGeneratedAttributeTo(this IVisitorContext context, string memberVariable)
    {
        var compilerGeneratedAttributeCtor = context.RoslynTypeSystem.SystemRuntimeCompilerServicesCompilerGeneratedAttribute.Ctor();
        var exps = CecilDefinitionsFactory.Attribute("compilerGenerated", memberVariable, context, compilerGeneratedAttributeCtor.MethodResolverExpression(context));
        context.WriteCecilExpressions(exps);
    }
}
