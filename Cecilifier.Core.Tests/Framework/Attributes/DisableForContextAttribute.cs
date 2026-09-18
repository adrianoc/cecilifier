using Cecilifier.Core.AST;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Cecilifier.Core.Tests.Framework.Attributes;

internal class DisableForContextAttribute<TContext> : FilterByContextBase<TContext>, IApplyToTest where TContext : IVisitorContext
{
    public void ApplyToTest(Test test)
    {
        if (!HasMatchingContext(test))
        {
            test.RunState = RunState.Runnable;
            return;
        }
        
        test.RunState = RunState.Ignored;
        test.Properties[PropertyNames.SkipReason].Add(IgnoreReason);
    }
}
