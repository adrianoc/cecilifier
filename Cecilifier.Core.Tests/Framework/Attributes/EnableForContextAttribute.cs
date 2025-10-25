#nullable enable

using Cecilifier.Core.AST;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Cecilifier.Core.Tests.Framework.Attributes;

/// <summary>
/// Configures which tests in test fixture are enabled/disabled for a specific Api Target (represented by the Target Api related context (See <typeparamref name="T" />) /> 
/// </summary>
/// <typeparam name="T">Context type associated with the target api./></typeparam>
internal class EnableForContextAttribute<T> : FilterByContextBase<T>, IApplyToTest where T : IVisitorContext
{
    /// <summary>
    /// Allows specific tests to be enabled (listed in the attribute constructor argument) or disabled (not listed)
    /// </summary>
    /// <param name="testNames">List of test names that *are* enabled. Any tests in a test fixture not listed here will not run (.i.e. will be disabled)</param>
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
