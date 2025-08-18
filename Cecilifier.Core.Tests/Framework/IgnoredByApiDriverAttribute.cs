#nullable enable

using System;
using System.Collections.Generic;
using Cecilifier.Core.ApiDriver;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal.Commands;

namespace Cecilifier.Core.Tests.Framework;

public class IgnoredByApiDriverAttribute<T> : Attribute, IWrapTestMethod where T : IILGeneratorApiDriver
{
    private readonly IList<string?> _testNamesToIgnore;
    public IgnoredByApiDriverAttribute(params string?[] testNamesToIgnore) => _testNamesToIgnore = testNamesToIgnore ?? [];
    
    public string IgnoreReason { get; set; } = string.Empty;
    
    public TestCommand Wrap(TestCommand command)
    {
        ITest? targetTest = command.Test;
        while (targetTest != null)
        {
            if (targetTest.Arguments.Length == 1 && targetTest.Arguments[0]?.GetType() == typeof(T) && (_testNamesToIgnore.Count == 0 || _testNamesToIgnore.Contains(targetTest.Name)))
            {
                Assert.Ignore(IgnoreReason);
                break;
            }
            targetTest = targetTest.Parent;
        }
        return command;
    }
}
