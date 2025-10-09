using System;
using System.Linq;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.CodeGeneration.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.AST
{
    internal static class NonCapturingLambdaProcessor
    {
        public static void InjectSyntheticMethodsForNonCapturingLambdas(IVisitorContext context, SyntaxNode node, string declaringTypeVarName)
        {
            var lambdas = node.DescendantNodesAndSelf().OfType<LambdaExpressionSyntax>();
            foreach (var lambda in lambdas)
            {
                if (!NoCapturedVariableValidator.IsValid(context, lambda))
                    continue;
                
                if (!IsValidConversion(context, lambda))
                {
                    context.EmitWarning($"Lambda to delegates conversion is only supported for Func<> and Action<>: {node.HumanReadableSummary()}", node);
                    continue;
                }

                InjectSyntheticMethodsFor(context, declaringTypeVarName, lambda);
            }
        }

        private static bool IsValidConversion(IVisitorContext context, LambdaExpressionSyntax lambda)
        {
            var typeInfo = context.GetTypeInfo(lambda);
            if (typeInfo.ConvertedType is not INamedTypeSymbol { IsGenericType: true } namedType)
                return false;

            return namedType.Name.StartsWith("Func") || namedType.Name.StartsWith("Action");
        }

        private static void InjectSyntheticMethodsFor(IVisitorContext context, string declaringTypeVarName, LambdaExpressionSyntax lambda)
        {
            using var _ = LineInformationTracker.Track(context, lambda);

            var returnType = context.SemanticModel.GetTypeInfo(lambda).ConvertedType;
            if (returnType is INamedTypeSymbol { IsGenericType: true } namedTypeSymbol)
            {
                if (namedTypeSymbol.FullyQualifiedName().Contains("System.Func"))
                {
                    returnType = namedTypeSymbol.TypeArguments[^1];
                }
                else if (returnType.FullyQualifiedName().Contains("System.Action"))
                {
                    returnType = context.RoslynTypeSystem.SystemVoid;
                }
            }
            else
            {
                throw new NotSupportedException($"Lambda not supported: {lambda.ToString()} ({lambda.GetLocation().GetLineSpan().StartLinePosition.Line}, {lambda.GetLocation().GetLineSpan().StartLinePosition.Character})");
            }

            var syntheticMethodName = lambda.GetSyntheticMethodName();
            var methodVar = context.Naming.SyntheticVariable(syntheticMethodName, ElementKind.Method);

            context.WriteNewLine();
            context.WriteComment($"Synthetic method for lambda expression: {lambda.HumanReadableSummary()}  @ ({lambda.GetLocation().GetLineSpan().StartLinePosition.Line}, {lambda.GetLocation().GetLineSpan().StartLinePosition.Character})");
            
            // Use the lambda string representation to register the newly added method definition as the declaring type to allow us to look it up latter.
            // Also, we only support non-capturing lambda expressions, so we handle those as static (even if the code does not mark them explicitly as such)
            // if/when we decide to support lambdas that captures variables/fields/params/etc we will probably need to revisit this.
            var methodExps = context.ApiDefinitionsFactory.Method(
                                                            context,
                                                            new BodiedMemberDefinitionContext(syntheticMethodName,methodVar, declaringTypeVarName, MemberOptions.None, IlContext.None),
                                                            lambda.ToString(),
                                                            syntheticMethodName,
                                                            syntheticMethodName,
                                                            "MethodAttributes.Public | MethodAttributes.Static",
                                                            lambda.ParameterList().Select(ParamSpecFor).ToArray(),
                                                            [],
                                                            ctx => ctx.TypeResolver.ResolveAny(returnType),
                                                            out var methodDefinitionVariable); 
            
            context.Generate(methodExps);

            ParameterSpec ParamSpecFor(ParameterSyntax parameter, int parameterIndex)
            {
                return new ParameterSpec(
                    parameter.Identifier.Text, 
                    context.TypeResolver.ResolveAny(namedTypeSymbol.TypeArguments[parameterIndex], ResolveTargetKind.Parameter),
                    RefKind.None,
                    parameter.Default != null ? Constants.ParameterAttributes.Optional : Constants.ParameterAttributes.None,
                    parameter.Accept(DefaultParameterExtractorVisitor.Instance));
            }

            var syntheticIlVar = context.Naming.ILProcessor(syntheticMethodName);
            context.Generate($"var {syntheticIlVar} = {methodVar}.Body.GetILProcessor();");
            context.WriteNewLine();

            using (context.DefinitionVariables.WithVariable(methodDefinitionVariable))
            {
                if (lambda.Block != null)
                {
                    StatementVisitor.Visit(context, syntheticIlVar, lambda.Block);
                    var controlFlow = context.SemanticModel.AnalyzeControlFlow(lambda.Block.ChildNodes().First(), lambda.Block.ChildNodes().Last());
                    if (!controlFlow.ReturnStatements.Any())
                        context.ApiDriver.WriteCilInstruction(context, syntheticIlVar, OpCodes.Ret);
                }
                else
                {
                    ExpressionVisitor.Visit(context, syntheticIlVar, lambda.Body);
                    context.ApiDriver.WriteCilInstruction(context, syntheticIlVar, OpCodes.Ret);
                }
            }
        }
    }
}
