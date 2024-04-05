using System;
using Cecilifier.Core.Variables;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class MethodDefinitionVariableTests
{
    [Test]
    public void EqualsTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName, [Values("p1", null)] string parameter)
    {
        var parameters = parameter != null ? new[] { parameter } : Array.Empty<string>();
        var tbt = new MethodDefinitionVariable(parentTypeName, "methodName", parameters, 0);
        Assert.That(tbt.Equals(tbt), Is.True);
    }

    [Test]
    public void EqualityOperatorsTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName, [Values(0, 1, 2)] byte typeParameterCount, [Values("p1", null)] string parameter)
    {
        var parameters = parameter != null ? [parameter] : Array.Empty<string>();
        var tbt = new MethodDefinitionVariable(parentTypeName, "methodName", parameters, typeParameterCount);
        var shouldBeEqual = new MethodDefinitionVariable(parentTypeName, "methodName", parameters, typeParameterCount);

        const byte TypeParameterCountNotMatchingAnyArgumentForTest = 42;
        var shouldNotBeEqual = new MethodDefinitionVariable(parentTypeName, "methodName", parameters, TypeParameterCountNotMatchingAnyArgumentForTest);

        Assert.That(tbt.Equals(shouldBeEqual), Is.True);
        Assert.That(tbt == shouldBeEqual, Is.True);
        Assert.That(tbt != shouldBeEqual, Is.False);
        
        Assert.That(tbt.Equals(shouldNotBeEqual), Is.False);
        Assert.That(tbt == shouldNotBeEqual, Is.False);
        Assert.That(tbt != shouldNotBeEqual, Is.True);
    }

    [Test]
    public void GetHashCodeTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName, [Values("p1", null)] string parameter)
    {
        var parameters = parameter != null ? [parameter] : Array.Empty<string>();
        var tbt = new MethodDefinitionVariable(parentTypeName, "methodName", parameters, 0);
        var shouldBeEqual = new MethodDefinitionVariable(parentTypeName, "methodName", parameters, 0);

        Assert.That(tbt.GetHashCode(), Is.EqualTo(shouldBeEqual.GetHashCode()));
    }

    [Test]
    public void ToStringTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName)
    {
        var tbt = new DefinitionVariable(parentTypeName, "memberName", kind);
        Assert.That(tbt.ToString(), Is.EqualTo($"(Kind = {kind}) {(parentTypeName != null ? $"{parentTypeName}." : "")}memberName"));
    }
}
