#nullable enable
using Cecilifier.Core.AST;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal.Commands;

namespace Cecilifier.Core.Tests.Framework.Attributes;

internal class ParameterizedResourceFilterAttribute<T> : FilterByContextBase<T>, IWrapTestMethod where T : IVisitorContext
{
    public ParameterizedResourceFilterAttribute(params string[] enabled) : base(enabled)
    {
    }

    public TestCommand Wrap(TestCommand command)
    {
        if (HasMatchingContext(command.Test) && command.Test.Arguments.Length > 0)
        {
            DisableTestIfNotApplicable(command.Test, (string) command.Test.Arguments[0]!);
        }
        return command;
    }
}
