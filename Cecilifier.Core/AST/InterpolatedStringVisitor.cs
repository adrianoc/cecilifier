using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;

namespace Cecilifier.Core.AST
{
    internal abstract class InterpolatedStringVisitor : SyntaxWalkerBase
    {
        public static InterpolatedStringVisitor For(InterpolatedStringExpressionSyntax node, IVisitorContext context, string ilVar, ExpressionVisitor expressionVisitor)
        {
            var numberOfArguments = node.Contents.OfType<InterpolationSyntax>().Count();
            return numberOfArguments <= 3
                ? new InterpolatedStringUpTo3ArgumentsVisitor(context, ilVar, expressionVisitor, numberOfArguments)
                : new InterpolatedStringWithMoreThan3ArgumentsVisitor(context, ilVar, expressionVisitor, numberOfArguments);
        }

        protected abstract IMethodSymbol GetStringFormatOverloadToCall();

        protected IEnumerable<IMethodSymbol> StringFormatOverloads => Context.RoslynTypeSystem.SystemString
                                                                                .GetMembers("Format")
                                                                                .OfType<IMethodSymbol>();

        protected InterpolatedStringVisitor(IVisitorContext context, string ilVar, ExpressionVisitor expressionVisitor) : base(context)
        {
            _ilVar = ilVar;
            _expressionVisitor = expressionVisitor;
        }

        public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            var lastInstructionBeforeInterpolatedString = Context.CurrentLine;

            BeforeVisitInterpolatedStringExpression();

            base.VisitInterpolatedStringExpression(node);

            Context.ApiDriver.WriteCilInstruction(Context, _ilVar, OpCodes.Ldstr, _computedFormat.ValueText());
            Context.MoveLineAfter(Context.CurrentLine, lastInstructionBeforeInterpolatedString);

            Context.AddCallToMethod(GetStringFormatOverloadToCall(), _ilVar, MethodDispatchInformation.MostLikelyVirtual);
        }

        protected virtual void BeforeVisitInterpolatedStringExpression() { }

        public override void VisitInterpolation(InterpolationSyntax node)
        {
            using var __ = LineInformationTracker.Track(Context, node);
            _computedFormat.Append($"{{{_currentParameterIndex++}}}");
            node.Expression.Accept(_expressionVisitor);

            // Expressions used in interpolated strings always report identity conversions but since
            // we are assigning then to objects, we need to check whether we need boxing.
            var conv = Context.SemanticModel.ClassifyConversion(node.Expression, Context.RoslynTypeSystem.SystemObject);
            if (conv.IsBoxing)
            {
                AddCilInstruction(_ilVar, OpCodes.Box, Context.GetTypeInfo(node.Expression).Type);
            }
        }

        public override void VisitInterpolatedStringText(InterpolatedStringTextSyntax node)
        {
            using var __ = LineInformationTracker.Track(Context, node);
            _computedFormat.Append(node.TextToken.ToFullString());
        }

        protected readonly string _ilVar;
        protected byte _currentParameterIndex = 0;

        private readonly StringBuilder _computedFormat = new();
        private readonly ExpressionVisitor _expressionVisitor;
    }
}
