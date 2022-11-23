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
        Assert.Throws<InvalidOperationException>(() => TypeHelpers.ResolveMethod(typeof(Foo).AssemblyQualifiedName, methodName, BindingFlags.Instance | BindingFlags.Public, string.Empty, Array.Empty<string>()));
    }
    
    [Test]
    public void ResolveField_Throws_IfTypeCannotBeFound()
    {
        Assert.Throws<Exception>(() => TypeHelpers.ResolveField("NonExistingType", "NotRelevant_TypeDoesNotExist"));
    }
    
    [Test]
    public void ResolveGenericMethod_Throws_IfNoMethodMatches()
    {
        Assert.Throws<MissingMethodException>(() => TypeHelpers.ResolveGenericMethod(typeof(string).FullName, "NotRelevant_TypeIsNotGeneric", BindingFlags.Public, Array.Empty<string>(), Array.Empty<ParamData>()));
    }
    
    [Test]
    public void ResolveGenericMethod_ReturnsNull_IfMethodNameMatchesButParameterListNot()
    {
        var resolveGenericMethod = TypeHelpers.ResolveGenericMethod(
            typeof(Foo).AssemblyQualifiedName, 
            "M", 
            BindingFlags.Public | BindingFlags.Instance, 
            new [] {"T"}, 
            new [] { new ParamData() { FullName = "NotImportant "} });
        
        Assert.IsNull(resolveGenericMethod);
    }

    public class Foo
    {
        public void M<T>(int i) { }
        public void M(int j) { }

        public Foo(int j) { }
    }
}
