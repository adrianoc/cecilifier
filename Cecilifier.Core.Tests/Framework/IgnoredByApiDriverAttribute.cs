#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace Cecilifier.Core.Tests.Framework;

/// <summary>
/// 
/// </summary>
/// <typeparam name="T"></typeparam>
/// <remarks>
/// The code assumes that the parent test fixture has been annotated with `TestFixtureSource` attribute
/// and that it injects the ILGeneratorApiDriver to be used by the test.
/// </remarks>
public class IgnoredByApiDriverAttribute<T> : Attribute, IWrapTestMethod where T : IILGeneratorApiDriver
{
    private readonly IList<string?> _testNamesToIgnore;
    public IgnoredByApiDriverAttribute(params string?[] testNamesToIgnore) => _testNamesToIgnore = testNamesToIgnore ?? [];
    
    public string IgnoreReason { get; set; } = string.Empty;
    
    public TestCommand Wrap(TestCommand command)
    {
        var parent = command.Test.Parent;
        while (parent is not TestFixture)
            parent = parent?.Parent;

        var apiDriverType = parent.Arguments[0]!.GetType(); 
        if (apiDriverType == typeof(T) && _testNamesToIgnore.Count == 0 || _testNamesToIgnore.Contains(apiDriverType.Name))
        {
            Assert.Ignore(IgnoreReason);
        }
        return command;
    }
}
