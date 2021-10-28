using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal class InterpolatedStringUpTo3ArgumentsVisitor : InterpolatedStringVisitor
    {
        private readonly int _numberOfArguments;

        public InterpolatedStringUpTo3ArgumentsVisitor(IVisitorContext context, string ilVar, ExpressionVisitor expressionVisitor, int numberOfArguments) : base(context, ilVar, expressionVisitor)
        {
            _numberOfArguments = numberOfArguments;
        }

        protected override IMethodSymbol GetStringFormatOverloadToCall()
        {
            return StringFormatOverloads.Single(f => f.Parameters[0].Type.SpecialType == SpecialType.System_String
                                                     && f.Parameters[1].Type.SpecialType == SpecialType.System_Object
                                                     && f.Parameters.Length == _numberOfArguments + 1);
        }
    }
    
    internal class InterpolatedStringWithMoreThan3ArgumentsVisitor : InterpolatedStringVisitor
    {
        public InterpolatedStringWithMoreThan3ArgumentsVisitor(IVisitorContext context, string ilVar, ExpressionVisitor expressionVisitor, int numberOfArguments) : base(context, ilVar, expressionVisitor)
        {
            _numberOfArguments = numberOfArguments;
        }

        protected override IMethodSymbol GetStringFormatOverloadToCall()
        {
            return StringFormatOverloads.Single(f => f.Parameters[0].Type.SpecialType == SpecialType.System_String
                                                     && f.Parameters[1].Type is IArrayTypeSymbol { ElementType: { SpecialType: SpecialType.System_Object } }
                                                     && f.Parameters.Length == 2);                
        }

        protected override void BeforeVisitInterpolatedStringExpression()
        {
            AddCilInstruction(_ilVar, OpCodes.Ldc_I4, _numberOfArguments);
            AddCilInstruction(_ilVar, OpCodes.Newarr, Context.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Object));
        }
 
        public override void VisitInterpolation(InterpolationSyntax node)
        {
            AddCilInstruction(_ilVar, OpCodes.Dup);
            AddCilInstruction(_ilVar, OpCodes.Ldc_I4, _currentParameterIndex);
            
            base.VisitInterpolation(node);
            AddCilInstruction(_ilVar, OpCodes.Stelem_Ref);
        }

        private readonly int _numberOfArguments;
    }

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
        
        protected IEnumerable<IMethodSymbol>  StringFormatOverloads => Context.SemanticModel.Compilation.GetSpecialType(SpecialType.System_String)
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
            
            AddCilInstruction(_ilVar, OpCodes.Ldstr, $"\"{_computedFormat}\"");
            Context.MoveLineAfter(Context.CurrentLine, lastInstructionBeforeInterpolatedString);
            
            AddMethodCall(_ilVar, GetStringFormatOverloadToCall(), false);
        }

        protected virtual void BeforeVisitInterpolatedStringExpression() { }

        public override void VisitInterpolation(InterpolationSyntax node)
        {
            _computedFormat.Append($"{{{_currentParameterIndex++}}}");
            node.Expression.Accept(_expressionVisitor);
            
            // Expressions used in interpolated strings always report identity conversions but since
            // we are assigning then to objects, we need to check whether we need boxing.
            var conv = Context.SemanticModel.ClassifyConversion(node.Expression, Context.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Object));
            if (conv.IsBoxing)
            {
                AddCilInstruction(_ilVar, OpCodes.Box, Context.GetTypeInfo(node.Expression).Type);               
            }
        }

        public override void VisitInterpolatedStringText(InterpolatedStringTextSyntax node)
        {
            _computedFormat.Append(node.TextToken.ToFullString());
        }

        protected readonly string _ilVar;
        protected byte _currentParameterIndex = 0;
        
        private readonly StringBuilder _computedFormat = new();
        private readonly ExpressionVisitor _expressionVisitor;
    }
}
