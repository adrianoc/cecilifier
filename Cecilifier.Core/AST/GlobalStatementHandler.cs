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

            var typeModifiers = CecilDefinitionsFactory.DefaultTypeAttributeFor(TypeKind.Class, false).AppendModifier("TypeAttributes.NotPublic | TypeAttributes.AutoLayout");
            typeVar = context.Naming.Type("topLevelStatements", ElementKind.Class);
            var typeExps = context.ApiDefinitionsFactory.Type(
                                                        context,
                                                        typeVar,
                                                        string.Empty, // global statements cannot be declared in namespace
                                                        "Program",
                                                        typeModifiers,
                                                        context.TypeResolver.Bcl.System.Object,
                                                        DefinitionVariable.NotFound, // Top level type has no outer type.
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
            var ilContext = context.ApiDriver.NewIlContext(context, "topLevelMain", methodVar);
            var methodExps = context.ApiDefinitionsFactory.Method(
                                                    context,
                                                    new BodiedMemberDefinitionContext("<Main>$", "programMain", methodVar, typeVar, MemberOptions.Static, ilContext),
                                                    "Program",
                                                    "MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.Static",
                                                    [new ParameterSpec("args", context.TypeResolver.MakeArrayType(context.RoslynTypeSystem.SystemString), RefKind.None, Constants.ParameterAttributes.None)],
                                                    [],
                                                    ctx => ctx.TypeResolver.ResolveAny(hasReturnStatement ? context.RoslynTypeSystem.SystemInt32 : context.RoslynTypeSystem.SystemVoid, ResolveTargetKind.ReturnType),
                                                    out _);
            context.Generate(methodExps);
            
            var mainBodyExps = context.ApiDefinitionsFactory.MethodBody(context, "topLevelMain", ilContext, [], []);
            context.Generate(mainBodyExps);

            ilVar = ilContext.VariableName; // TODO: (remove) This forces the related ILProcessor variable to be emitted.
            NonCapturingLambdaProcessor.InjectSyntheticMethodsForNonCapturingLambdas(context, firstGlobalStatement, typeVar);
        }

        public bool HandleGlobalStatement(GlobalStatementSyntax node)
        {
            using (context.DefinitionVariables.WithCurrent("<global namespace>", "Program", VariableMemberKind.Type, typeVar))
            using (context.DefinitionVariables.WithCurrentMethod("Program", "<Main>$", [], 0, methodVar))
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

            context.OnFinishedTypeDeclaration();
            return true;

            bool IsLastGlobalStatement(CompilationUnitSyntax compilation, int index)
            {
                return compilation.Members.Count == (index + 1) || !root.Members[index + 1].IsKind(SyntaxKind.GlobalStatement);
            }
        }

        public string MainMethodDefinitionVariable => methodVar;

        private readonly string ilVar;
        private readonly string methodVar;
        private readonly string typeVar;
        private readonly IVisitorContext context;
    }
}
