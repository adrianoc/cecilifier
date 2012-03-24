using System;
using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
    //TODO: Introduce CilEmitterSyntaxWalker
    // class ConstructorInitializerVisitor : CilEmiterSyntaxWalker, IMemoryLocationResolver
    class ConstructorInitializerVisitor : SyntaxWalkerBase, IMemoryLocationResolver
    {
        internal ConstructorInitializerVisitor(IVisitorContext ctx, string ilVar) : base(ctx)
        {
        	this.ilVar = ilVar;
        }

        protected override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            base.VisitConstructorInitializer(node);
            
            var info = Context.SemanticModel.GetSemanticInfo(node);
            var targetCtor = (MethodSymbol)info.Symbol;

			AddCilInstruction(ilVar, OpCodes.Call, targetCtor.MethodResolverExpression(Context));
            
            var declaringType = (BaseTypeDeclarationSyntax) node.Parent.Parent;
            Context.SetDefaultCtorInjectorFor(declaringType, delegate { });

            //FIXME: Fix ctor construction
            //
            // 1. Field initialization
            // 2. Ctor initializer
            //    2.1 Load parameters
            //    2.2 Call base/this ctor
            // 3. If no ctor initializer call base ctor
            // 4. Ctor body
        }

		protected override void VisitArgument(ArgumentSyntax node)
		{
			new ExpressionVisitor(Context, ilVar).Visit(node.Expression);
			argIndex++;
		}

		//private string MethodResolverExpression(MethodSymbol method)
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
 
        private int argIndex = 0;
        private int localVarIndex = 0;
    	private string ilVar;
    }
}
