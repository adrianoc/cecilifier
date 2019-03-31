using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    //TODO: Introduce CilEmitterSyntaxWalker
    // class ConstructorInitializerVisitor : CilEmiterSyntaxWalker, IMemoryLocationResolver
    internal class ConstructorInitializerVisitor : SyntaxWalkerBase, IMemoryLocationResolver
    {
        //private string MethodResolverExpression(IMethodSymbol method)
        //{
        //    //FIXME: Handle forward declarations..
        //    //       One option is to not generate cecil calls as we visit the AST; instead
        //    //       we could "accumulate" and generate later
        //    if (method.ContainingAssembly == Context.SemanticModel.Compilation.Assembly)
        //    {
        //        //FIXME: Keep the name of the variables used to construct types/members in a map
        //        return LocalVariableNameFor(method.ContainingType.Name, method.Name.Replace(".", ""), method.MangleName());
        //    }

        //    var declaringTypeName =
        //        method.ContainingType.ToDisplayString(
        //                    new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));

        //    return String.Format("assembly.MainModule.Import(ResolveMethod(\"{0}\", \"{1}\", \"{2}\"{3}))",
        //                         method.ContainingAssembly.AssemblyName.FullName,
        //                         declaringTypeName,
        //                         method.Name,
        //                         method.Parameters.Aggregate("", (acc, curr) => ", \"" + curr.Name + "\""));
        //}

        private readonly string ilVar;

        internal ConstructorInitializerVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
            this.ilVar = ilVar;
        }

        public override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            base.VisitConstructorInitializer(node);

            var info = Context.SemanticModel.GetSymbolInfo(node);
            var targetCtor = (IMethodSymbol) info.Symbol;

            AddCilInstruction(ilVar, OpCodes.Call, targetCtor.MethodResolverExpression(Context));

            var declaringType = (BaseTypeDeclarationSyntax) node.Parent.Parent;

            //FIXME: Fix ctor construction
            //
            // 1. Field initialization
            // 2. Ctor initializer
            //    2.1 Load parameters
            //    2.2 Call base/this ctor
            // 3. If no ctor initializer call base ctor
            // 4. Ctor body
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            ExpressionVisitor.Visit(Context, ilVar, node.Expression);
        }
    }
}
