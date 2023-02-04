using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal static class NonCapturingLambdaProcessor
    {
        public static void InjectSyntheticMethodsForNonCapturingLambdas(IVisitorContext context, SyntaxNode node, string declaringTypeVarName)
        {
            var lambdas = node.DescendantNodesAndSelf().OfType<LambdaExpressionSyntax>();
            foreach (var lambda in lambdas)
            {
                var captures = CapturesFrom(context, lambda); 
                if (captures.Any())
                {
                    context.WriteComment($"Lambdas that captures context are not supported. Lambda expression '{lambda}' captures {string.Join(",", captures)}");
                    continue;
                }

                if (!IsValidConversion(context, lambda))
                {
                    context.WriteComment("Lambda to delegates conversion is only supported for Func<> and Action<>");
                    continue;
                }
            
                InjectSyntheticMethodsFor(context, declaringTypeVarName, lambda);
            }
        }

        private static bool IsValidConversion(IVisitorContext context, LambdaExpressionSyntax lambda)
        {
            var typeInfo = context.GetTypeInfo(lambda);
            if (typeInfo.ConvertedType is not INamedTypeSymbol {IsGenericType : true} namedType)
                return false;

            return namedType.Name.StartsWith("Func") || namedType.Name.StartsWith("Action");
        }

        private static string[] CapturesFrom(IVisitorContext context, LambdaExpressionSyntax lambda)
        {
            var captured = new List<string>();
            foreach (var identifier in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var symbolInfo = ModelExtensions.GetSymbolInfo(context.SemanticModel, identifier);
                if ((symbolInfo.Symbol?.Kind == SymbolKind.Parameter || symbolInfo.Symbol?.Kind == SymbolKind.Local) && symbolInfo.Symbol?.ContainingSymbol is IMethodSymbol method && method.MethodKind != MethodKind.AnonymousFunction)
                {
                    captured.Add(identifier.Identifier.Text);
                }
                else if (symbolInfo.Symbol?.Kind == SymbolKind.Field && symbolInfo.Symbol?.ContainingSymbol is ITypeSymbol)
                {
                    captured.Add(identifier.Identifier.Text);
                }
            }

            return captured.ToArray();
        }

        private static void InjectSyntheticMethodsFor(IVisitorContext context, string declaringTypeVarName, LambdaExpressionSyntax lambda)
        {
            using var _ = LineInformationTracker.Track(context, lambda);
            
            var returnType = ModelExtensions.GetTypeInfo(context.SemanticModel, lambda).ConvertedType;
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
                throw new Exception($"Lambda not supported: {lambda.ToString()} ({lambda.GetLocation().GetLineSpan().StartLinePosition.Line}, {lambda.GetLocation().GetLineSpan().StartLinePosition.Character})");
            }

            var lambdaSourcePosition = lambda.GetLocation().GetLineSpan().StartLinePosition;
            var syntheticMethodName = $"Lambda_{lambdaSourcePosition.Line}_{lambdaSourcePosition.Character}";
            var methodVar = context.Naming.SyntheticVariable(syntheticMethodName, ElementKind.Method);

            // We only support non-capturing lambda expressions so we handle those as static (even if the code does not mark them explicitly as such)
            // if/when we decide to support lambdas that captures variables/fields/params/etc we will probably need to revisit this.
            var methodExps = CecilDefinitionsFactory.Method(context, methodVar, syntheticMethodName, "MethodAttributes.Public | MethodAttributes.Static", returnType, false, Array.Empty<TypeParameterSyntax>());
        
            context.WriteNewLine();
            context.WriteComment($"Synthetic method for lambda expression: {lambda.HumanReadableSummary()}  @ ({lambda.GetLocation().GetLineSpan().StartLinePosition.Line}, {lambda.GetLocation().GetLineSpan().StartLinePosition.Character})");
            foreach (var exp in methodExps)
            {
                context.WriteCecilExpression(exp);
                context.WriteNewLine();
            }

            // Add parameters...
            var i = 0;
            foreach (var parameter in lambda.ParameterList())
            {
                context.WriteNewLine();
                context.WriteComment($"Parameter: {parameter}");
                var resolvedParamType = context.TypeResolver.Resolve(namedTypeSymbol.TypeArguments[i++]);
                var paramExps = CecilDefinitionsFactory.Parameter(
                    parameter.Identifier.Text, 
                    RefKind.None, 
                    isParams: false, 
                    methodVar, 
                    context.Naming.SyntheticVariable(parameter.Identifier.Text, ElementKind.Parameter),
                    resolvedParamType,
                    parameter.Default != null ? Constants.ParameterAttributes.Optional : Constants.ParameterAttributes.None,
                    parameter.Accept(DefaultParameterExtractorVisitor.Instance));
                
                foreach (var paramExp in paramExps)
                {
                    context.WriteCecilExpression(paramExp);
                    context.WriteNewLine();
                }
            }

            var syntheticIlVar = context.Naming.ILProcessor(syntheticMethodName);
            context.WriteCecilExpression($"var {syntheticIlVar} = {methodVar}.Body.GetILProcessor();");
            context.WriteNewLine();

            // Register the newly introduced method so we can replace the lambda with the method later.
            // Use the lambda string representation as the name of the method in order to allow us to look it up.
            using (context.DefinitionVariables.WithCurrentMethod(string.Empty, lambda.ToString(), Array.Empty<string>(), methodVar))
            {
                if (lambda.Block != null)
                {
                    StatementVisitor.Visit(context, syntheticIlVar, lambda.Block);
                    var controlFlow = context.SemanticModel.AnalyzeControlFlow(lambda.Block.ChildNodes().First(), lambda.Block.ChildNodes().Last());
                    if (!controlFlow.ReturnStatements.Any())
                        context.EmitCilInstruction(syntheticIlVar, OpCodes.Ret);
                }
                else
                {
                    ExpressionVisitor.Visit(context, syntheticIlVar, lambda.Body);
                    context.EmitCilInstruction(syntheticIlVar, OpCodes.Ret);
                }    
            }
            
            context.WriteNewLine();
            context.WriteCecilExpression($"{declaringTypeVarName}.Methods.Add({methodVar});");
        }
    }
}
