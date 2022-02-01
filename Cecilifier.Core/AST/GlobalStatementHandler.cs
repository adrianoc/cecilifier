using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
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

            hasReturnStatement = firstGlobalStatement.Parent.DescendantNodes().Any(node => node.IsKind(SyntaxKind.ReturnStatement));

            var typeModifiers = CecilDefinitionsFactory.DefaultTypeAttributeFor(SyntaxKind.ClassDeclaration, false).AppendModifier("TypeAttributes.NotPublic | TypeAttributes.AutoLayout");
            typeVar = context.Naming.Type("topLevelStatements", ElementKind.Class);
            var typeExps = CecilDefinitionsFactory.Type(
                context, 
                typeVar, 
                "Program", 
                typeModifiers, 
                context.TypeResolver.Bcl.System.Object, 
                false, 
                Array.Empty<string>());
                
            methodVar = context.Naming.SyntheticVariable("topLevelStatements", ElementKind.Method);
            var methodExps = CecilDefinitionsFactory.Method(
                context, 
                methodVar, 
                "<Main>$", 
                "MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.Static", 
                hasReturnStatement ? context.RoslynTypeSystem.SystemInt32 : context.RoslynTypeSystem.SystemVoid,
                false,
                Array.Empty<TypeParameterSyntax>());
            
            var paramVar = context.Naming.Parameter("args", "Main");
            var mainParametersExps = CecilDefinitionsFactory.Parameter(
                "args", 
                RefKind.None, 
                false, 
                methodVar, 
                paramVar, 
                $"{context.TypeResolver.Bcl.System.String}.MakeArrayType()");

            ilVar = context.Naming.ILProcessor("topLevelMain", "Main");
            var mainBodyExps = CecilDefinitionsFactory.MethodBody(methodVar, ilVar, Array.Empty<InstructionRepresentation>());
                
            WriteCecilExpressions(typeExps);
            WriteCecilExpressions(methodExps);
            WriteCecilExpressions(mainParametersExps);
            WriteCecilExpressions(mainBodyExps);
                
            WriteCecilExpression($"{typeVar}.Methods.Add({methodVar});");
            
            new ConstructorDeclarationVisitor(context).DefaultCtorInjector2(typeVar, "Program");
        }

        public bool HandleGlobalStatement(GlobalStatementSyntax node)
        {
            using (context.DefinitionVariables.WithCurrent(string.Empty, "Program", VariableMemberKind.Type, typeVar))
            using (context.DefinitionVariables.WithCurrentMethod("Program", "<Main>$", Array.Empty<string>(), methodVar))
            {
                if (node.Statement.IsKind(SyntaxKind.LocalFunctionStatement))
                {
                    context.WriteComment($"Local function: {node.HumanReadableSummary()}");
                    StatementVisitor.Visit(context, ilVar, node.Statement);
                    context.WriteComment("End of local function.");
                    context.WriteNewLine();
                }
                else
                    StatementVisitor.Visit(context, ilVar, node.Statement);
            }
            
            var root = (CompilationUnitSyntax) node.SyntaxTree.GetRoot();
            var globalStatementIndex = root.Members.IndexOf(node);
            
            if (!IsLastGlobalStatement(root, globalStatementIndex))
            {
                return false;
            }

            if (!node.Statement.IsKind(SyntaxKind.ReturnStatement))
                context.WriteCecilExpression($"{methodVar}.Body.Instructions.Add({ilVar}.Create(OpCodes.Ret));");
            
            return true;

            bool IsLastGlobalStatement(CompilationUnitSyntax compilation, int globalStatementIndex)
            {
                return compilation.Members.Count == (globalStatementIndex + 1) || !root.Members[globalStatementIndex + 1].IsKind(SyntaxKind.GlobalStatement);
            }
        }

        private void WriteCecilExpressions(IEnumerable<string> expressions)
        {
            foreach (var exp in expressions)
            {
                WriteCecilExpression(exp);
            }
        }

        private void WriteCecilExpression(string expression)
        {
            context.WriteCecilExpression(expression);
            context.WriteNewLine();
        }
        
        private readonly string ilVar;
        private readonly string methodVar;
        private readonly string typeVar;
        private readonly IVisitorContext context;
        private bool hasReturnStatement;
    }
}
