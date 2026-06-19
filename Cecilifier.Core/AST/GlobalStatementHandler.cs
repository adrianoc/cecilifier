using System.Linq;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    public class GlobalStatementHandler
    {
        internal GlobalStatementHandler(IVisitorContext context, GlobalStatementSyntax firstGlobalStatement)
        {
            this.context = context;

            var hasReturnStatement = firstGlobalStatement.Parent!.DescendantNodes().Any(node => node.IsKind(SyntaxKind.ReturnStatement));

            var typeModifiers = CecilDefinitionsFactory.DefaultTypeAttributeFor(TypeKind.Class, false).AppendEnumFlag("TypeAttributes.NotPublic | TypeAttributes.AutoLayout");
            typeVar = context.Naming.Type("topLevelStatements", ElementKind.Class);
            var typeExps = context.ApiDefinitionsFactory.Type(
                                                        context,
                                                        new MemberDefinitionContext("Program", typeVar, null /*Top level type has no outer type.*/),
                                                        string.Empty, // global statements cannot be declared in namespaces
                                                        typeModifiers,
                                                        context.RoslynTypeSystem.SystemObject,
                                                        false,
                                                        [], 
                                                        [], 
                                                        []);
            context.Generate(typeExps);

            new ConstructorDeclarationVisitor(context)
                .DefaultCtorInjector(
                    typeVar,
                    "Program",
                    "MethodAttributes.Public",
                    context.MemberResolver.ResolveDefaultConstructor(context.RoslynTypeSystem.SystemObject, typeVar),
                    false,
                    null);

            methodVar = context.Naming.SyntheticVariable("topLevelMain", ElementKind.Method);
            ilVar = context.ApiDriver.NewIlContext(context, "topLevelMain", methodVar);
            var methodExps = context.ApiDefinitionsFactory.Method(
                                                    context,
                                                    new BodiedMemberDefinitionContext("<Main>$", "programMain", methodVar, typeVar, MemberOptions.Static, ilVar),
                                                    "Program",
                                                    "MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.Static",
                                                    [new ParameterSpec("args", context.TypeResolver.MakeArrayType(context.RoslynTypeSystem.SystemString, ResolveTargetKind.Parameter), RefKind.None, Constants.ParameterAttributes.None)],
                                                    [],
                                                    ctx => ctx.TypeResolver.Resolve(hasReturnStatement ? context.RoslynTypeSystem.SystemInt32 : context.RoslynTypeSystem.SystemVoid, ResolveTargetKind.ReturnType),
                                                    out _);
            context.Generate(methodExps);
            
            var mainBodyExps = context.ApiDefinitionsFactory.MethodBody(context, "topLevelMain", ilVar, [], []);
            context.Generate(mainBodyExps);

            NonCapturingLambdaProcessor.InjectSyntheticMethodsForNonCapturingLambdas(context, firstGlobalStatement, typeVar);
        }

        public bool HandleGlobalStatement(GlobalStatementSyntax node)
        {
            using (context.DefinitionVariables.WithCurrent("<global namespace>", "Program", VariableMemberKind.Type, typeVar))
            using (context.DefinitionVariables.WithCurrentMethod("Program", "<Main>$", [], [], methodVar))
            {
                if (node.Statement.IsKind(SyntaxKind.LocalFunctionStatement))
                {
                    context.WriteComment($"Local function: {node.HumanReadableSummary()}");
                    StatementVisitor.Visit(context, ilVar, node);
                    context.WriteComment("End of local function.");
                    context.WriteNewLine();
                }
                else
                    StatementVisitor.Visit(context, ilVar, node);
            }

            var root = (CompilationUnitSyntax) node.SyntaxTree.GetRoot();
            var globalStatementIndex = root.Members.IndexOf(node);

            if (!IsLastGlobalStatement(root, globalStatementIndex))
            {
                return false;
            }

            if (!node.Statement.IsKind(SyntaxKind.ReturnStatement))
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ret);

            context.WriteNewLine();
            
            context.OnFinishedTypeDeclaration(null);
            return true;

            bool IsLastGlobalStatement(CompilationUnitSyntax compilation, int index)
            {
                return compilation.Members.Count == (index + 1) || !root.Members[index + 1].IsKind(SyntaxKind.GlobalStatement);
            }
        }

        public string MainMethodDefinitionVariable => methodVar;

        private readonly IlContext ilVar;
        private readonly string methodVar;
        private readonly string typeVar;
        private readonly IVisitorContext context;
    }
}
