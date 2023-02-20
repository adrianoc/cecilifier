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
        var tbt = new MethodDefinitionVariable(parentTypeName, "methodName", parameters);
        Assert.That(tbt.Equals(tbt), Is.True);
    }

    [Test]
    public void EqualityOperatorsTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName, [Values("p1", null)] string parameter)
    {
        var parameters = parameter != null ? new[] { parameter } : Array.Empty<string>();
        var tbt = new MethodDefinitionVariable(parentTypeName, "methodName", parameters);
        var shouldBeEqual = new MethodDefinitionVariable(parentTypeName, "methodName", parameters);

        Assert.That(tbt.Equals(shouldBeEqual), Is.True);
        Assert.That(tbt == shouldBeEqual, Is.True);
        Assert.That(tbt != shouldBeEqual, Is.False);
    }

    [Test]
    public void GetHashCodeTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName, [Values("p1", null)] string parameter)
    {
        var parameters = parameter != null ? new[] { parameter } : Array.Empty<string>();
        var tbt = new MethodDefinitionVariable(parentTypeName, "methodName", parameters);
        var shouldBeEqual = new MethodDefinitionVariable(parentTypeName, "methodName", parameters);

        Assert.That(tbt.GetHashCode(), Is.EqualTo(shouldBeEqual.GetHashCode()));
    }

    [Test]
    public void ToStringTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName)
    {
        var tbt = new DefinitionVariable(parentTypeName, "memberName", kind);
        Assert.That(tbt.ToString(), Is.EqualTo($"(Kind = {kind}) {(parentTypeName != null ? $"{parentTypeName}." : "")}memberName"));
    }
}
