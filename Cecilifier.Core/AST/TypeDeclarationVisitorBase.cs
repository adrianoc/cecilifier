using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class TypeDeclarationVisitorBase : SyntaxWalkerBase
    {
        internal TypeDeclarationVisitorBase(IVisitorContext ctx) : base(ctx)
        {
        }

        protected void HandleAttributesInTypeDeclaration(BaseTypeDeclarationSyntax node, string varName)
        {
            if (node.AttributeLists.Count == 0)
            {
                return;
            }

            foreach (var attribute in node.AttributeLists.SelectMany(al => al.Attributes))
            {
                var attrsExp = CecilDefinitionsFactory.Attribute(varName, Context, attribute, (attrType, attrArgs) =>
                {
                    var typeVar = ResolveTypeLocalVariable(attrType.Name);
                    if (typeVar == null)
                    {
                        //attribute is not declared in the same assembly....
                        var ctorArgumentTypes = $"new Type[{attrArgs.Length}] {{ {string.Join(",", attrArgs.Select(arg => $"typeof({Context.GetTypeInfo(arg.Expression).Type.Name})"))} }}";
                        return $"assembly.MainModule.ImportReference(typeof({attrType.FullyQualifiedName()}).GetConstructor({ctorArgumentTypes}))";
                    }

                    // Attribute is defined in the same assembly. We need to find the variable that holds its "ctor declaration"
                    var attrCtor = attrType.GetMembers().OfType<IMethodSymbol>().SingleOrDefault(m => m.MethodKind == MethodKind.Constructor && m.Parameters.Length == attrArgs.Length);
                    var attrCtorVar = MethodExtensions.LocalVariableNameFor(attrType.Name, "ctor", attrCtor.MangleName());

                    return attrCtorVar;
                });

                AddCecilExpressions(attrsExp);
            }
        }
    }
}
