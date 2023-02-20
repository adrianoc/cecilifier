using Cecilifier.Core.Variables;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class DefinitionVariableTests
{
    [Test]
    public void EqualsTests([Values] VariableMemberKind kind)
    {
        var tbt = new DefinitionVariable("parent", "memberName", kind);
        Assert.That(tbt.Equals(tbt), Is.True);
    }

    [Test]
    public void GetHashCodeTests([Values] VariableMemberKind kind)
    {
        var tbt = new DefinitionVariable("parent", "memberName", kind);
        var shouldBeTheSame = new DefinitionVariable("parent", "memberName", kind);
        Assert.That(tbt.GetHashCode(), Is.EqualTo(shouldBeTheSame.GetHashCode()));
    }

    [Test]
    public void ToStringTests([Values] VariableMemberKind kind, [Values("Parent", null)] string parentTypeName)
    {
        var tbt = new DefinitionVariable(parentTypeName, "memberName", kind);
        Assert.That(tbt.ToString(), Is.EqualTo($"(Kind = {kind}) {(parentTypeName != null ? $"{parentTypeName}." : "")}memberName"));
    }
}
