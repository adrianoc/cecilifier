#nullable enable

using Cecilifier.Core.AST;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Cecilifier.Core.Tests.Framework.Attributes;

internal class EnableForContextAttribute<T> : FilterByContextBase<T>, IApplyToTest where T : IVisitorContext
{
    public EnableForContextAttribute(params string[] testNames) : base(testNames)
    {
    }

    public void ApplyToTest(Test test)
    {
        test.RunState = RunState.Runnable;
        if (!test.HasChildren)
            return;

        if (!HasMatchingContext(test))
            return;
            
        foreach (var childTest in test.Tests)
        {
            DisableTestIfNotApplicable(childTest, childTest.Name);
        }
    }
}
