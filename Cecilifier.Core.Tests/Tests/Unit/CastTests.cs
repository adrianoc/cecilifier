using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class CastTests : CecilifierUnitTestBase
{
    [Test]
    public void Unbox()
    {
        var result = RunCecilifier("int UnboxIt(object o) => (int) o;");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match("""
                                                                 (il_unboxIt_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                                                 \s+\1Unbox_Any, assembly.MainModule.TypeSystem.Int32\);
                                                                 """));
    }
    
    [TestCase("i", TestName = "Implicit boxing")]
    [TestCase("(object) i", TestName = "Explicit boxing")]
    public void Box(string expression)
    {
        var result = RunCecilifier($"object BoxIt(int i) => {expression};");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match("""
                                                                 (il_boxIt_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                                                 \s+\1Box, assembly.MainModule.TypeSystem.Int32\);
                                                                 """));
    }
}
