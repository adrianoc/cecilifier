using System;
using System.Reflection;
using Cecilifier.Runtime;
using Mono.Cecil;
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

    [Test]
    public void TryMapAssemblyFromType_ResolveInnerTypes()
    {
        var fixer = new PrivateCorlibFixerMixin(ModuleDefinition.CreateModule("Test", ModuleKind.Dll));
        var result = fixer.TryMapAssemblyFromType("System.Collections.Generic.List`1+Enumerator", out _);
        Assert.That(result, Is.True);
    }

    public class Foo
    {
        public void M<T>(int i) { }
        public void M(int j) { }

        public Foo(int j) { }
    }
}
