#nullable enable

using System;
using System.Collections.Generic;
using Cecilifier.Core.AST;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Cecilifier.Core.Tests.Framework;

public class EnableForContextAttribute<T> : Attribute, IApplyToTest where T : IVisitorContext
{
    private readonly HashSet<string> _enabledTests;
    public EnableForContextAttribute(params string[] testNames) => _enabledTests = [..testNames];
    
    public string IgnoreReason { get; set; } = string.Empty;
    
    public void ApplyToTest(Test test)
    {
        test.RunState = RunState.Runnable;
        if (!test.HasChildren)
            return;
        
        var filterType = test.TypeInfo?.Type.GenericTypeArguments[0]!;
        if (filterType.Name != GetType().GenericTypeArguments[0].Name)
            return;
        
        foreach (var childTest in test.Tests)
        {
            if (!_enabledTests.Contains(childTest.Name))
            {
                ((Test) childTest).RunState = RunState.Ignored;
               childTest.Properties[PropertyNames.SkipReason].Add(IgnoreReason);
            }
        }
    }
}
