using System.Linq;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Extensions
{
    static class MethodExtensions
    {
        public static string MangleName(this BaseMethodDeclarationSyntax method)
        {
            return method.ParameterList.Parameters.Aggregate("", (acc, curr) => acc + curr.TypeOpt.PlainName);
        }
        
        public static string MangleName(this MethodSymbol method)
        {
            return method.Parameters.Aggregate("", (acc, curr) => acc + curr.Type.Name.ToLower());
        }

    }
}
