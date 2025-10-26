#nullable enable
using System;
using Cecilifier.Core.AST;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal.Commands;

namespace Cecilifier.Core.Tests.Framework.Attributes;

/// <summary>
/// Configures which tests in an integration test fixture are enabled/disabled for a specific Api Target (represented by the Target Api related context (See <typeparamref name="T" />) /> 
/// </summary>
/// <typeparam name="T">Context type associated with the target api./></typeparam>
[AttributeUsage(AttributeTargets.Method)]
internal class ParameterizedResourceFilterAttribute<T> : FilterByContextBase<T>, IWrapTestMethod where T : IVisitorContext
{
    public ParameterizedResourceFilterAttribute(params string[] enabled) : base(enabled)
    {
    }

    public TestCommand Wrap(TestCommand command)
    {
        if (HasMatchingContext(command.Test) && command.Test.Arguments.Length > 0)
        {
            if(DisableTestIfNotApplicable(command.Test, (string) command.Test.Arguments[0]!))
                return new SkipCommand(command.Test);
        }
        return command;
    }
}
