using System;
using System.Reflection;
using Cecilifier.Runtime;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class TypeHelpersTests
{
    [TestCase(".ctor")]
    [TestCase("M")]
    public void ResolveMethod_Throws_IfTypeCannotBeFound(string methodName)
    {
        Assert.Throws<InvalidOperationException>(() => TypeHelpers.ResolveMethod(typeof(Foo), methodName, BindingFlags.Instance | BindingFlags.Public, Array.Empty<string>()));
    }
    
    [Test]
    public void ResolveField_Throws_IfTypeCannotBeFound()
    {
        Assert.Throws<Exception>(() => TypeHelpers.ResolveField("NonExistingType", "NotRelevant_TypeDoesNotExist"));
    }
    
    public class Foo
    {
        public void M<T>(int i) { }
        public void M(int j) { }

        public Foo(int j) { }
    }
}
