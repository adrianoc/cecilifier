#nullable enable
using System;
using System.Collections.Generic;
using Cecilifier.Core.AST;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Cecilifier.Core.Tests.Framework.Attributes;

internal class FilterByContextBase<T> : Attribute where T : IVisitorContext
{
    private readonly HashSet<string> _enabledTests;
    public FilterByContextBase(params string[] testNames) => _enabledTests = [..testNames];
    
    public string IgnoreReason { get; init; } = string.Empty;
    
    protected bool HasMatchingContext(Test test)
    {
        test.RunState = RunState.Runnable;
        if (test.TypeInfo?.Type.GenericTypeArguments.Length == 0)
            return false;
        
        var filterType = test.TypeInfo?.Type.GenericTypeArguments[0]!;
        return filterType.Name == GetType().GenericTypeArguments[0].Name;
    }

    protected bool DisableTestIfNotApplicable(ITest test, string testName)
    {
        if (_enabledTests.Contains(testName))
            return false;
        
        ((Test) test).RunState = RunState.Ignored;
        test.Properties[PropertyNames.SkipReason].Add(IgnoreReason);

        return true;
    }
}
