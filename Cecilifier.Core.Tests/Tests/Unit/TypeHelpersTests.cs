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

    [Test]
    public void ResolveGenericInstanceMethod_IsAbleToResolve()
    {
        var resolved = TypeHelpers.ResolveGenericMethodInstance(
            typeof(Foo).AssemblyQualifiedName, 
            "M", 
            BindingFlags.Instance | BindingFlags.Public,
            [new ParamData { FullName = "System.Int32", IsArray = false, IsTypeParameter = false}],
            ["System.Int32"]);

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved.Name, Is.EqualTo("M"));
        Assert.That(resolved.GetGenericArguments().Length, Is.EqualTo(1));
    }
    
    [Test]
    public void Debug()
    {
        var resolved = TypeHelpers.ResolveGenericMethodInstance(
                typeof(Array).AssemblyQualifiedName, 
                "BinarySearch", 
                BindingFlags.Default|BindingFlags.Static|BindingFlags.Public, 
                new ParamData[]
                {
                    new() { FullName="T", IsArray=true, IsTypeParameter=false } ,
                    new() { FullName="T", IsArray=false, IsTypeParameter=true }
                }, new [] { "T" }) ;
        
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved.Name, Is.EqualTo("BinarySearch"));
        Assert.That(resolved.GetGenericArguments().Length, Is.EqualTo(1));
    }

    public class Foo
    {
        public void M<T>(int i) { }
        public void M(int j) { }

        public Foo(int j) { }
    }
}
