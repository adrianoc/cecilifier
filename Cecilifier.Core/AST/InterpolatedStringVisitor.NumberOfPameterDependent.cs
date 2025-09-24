using System.Linq;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            Context.ApiDriver.WriteCilInstruction(Context, _ilVar, OpCodes.Ldc_I4, _numberOfArguments);
            Context.ApiDriver.WriteCilInstruction(Context, _ilVar, OpCodes.Newarr, Context.TypeResolver.Bcl.System.Object);
        }

        public override void VisitInterpolation(InterpolationSyntax node)
        {
            Context.ApiDriver.WriteCilInstruction(Context, _ilVar, OpCodes.Dup);
            Context.ApiDriver.WriteCilInstruction(Context, _ilVar, OpCodes.Ldc_I4, _currentParameterIndex);

            base.VisitInterpolation(node);
            Context.ApiDriver.WriteCilInstruction(Context, _ilVar, OpCodes.Stelem_Ref);
        }

        private readonly int _numberOfArguments;
    }
}
