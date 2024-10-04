using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class CollectionExpressionTests : CecilifierUnitTestBase
{
    [Test]
    public void ArrayWith3OrMoreElements_UsesOptimizedInitialization()
    {
        var result = RunCecilifier("int[] mediumArray = [1, 2, 3];");
        var cecilified = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilified, Does.Match("//__StaticArrayInitTypeSize=12 struct."));
        Assert.That(cecilified, Does.Match(@"var fld_arrayInitializerData_\d+ = new FieldDefinition\(""[A-F0-9]+"",.+, st_rawDataTypeVar_\d+\);"));
        Assert.That(cecilified, Does.Match(@"il_topLevelMain_3.Emit\(OpCodes.Ldtoken, fld_arrayInitializerData_\d+\);"));
    }
    
    [Test]
    public void ArrayWith2OrLessElements_DoesNotUsesOptimizedInitialization()
    {
        var result = RunCecilifier("int[] mediumArray = [1, 2];");
        var cecilified = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilified, Does.Not.Match(@"//__StaticArrayInitTypeSize=\d+ struct."));
        Assert.That(cecilified, Does.Match("""
                                           //int\[\] mediumArray = \[1, 2\];
                                           \s+var (?<array>l_mediumArray_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32.MakeArrayType\(\)\);
                                           \s+m_topLevelStatements_1.Body.Variables.Add\(\k<array>\);
                                           \s+(?<il>il_topLevelMain_\d+.Emit\(OpCodes\.)Ldc_I4, 2\);
                                           \s+\k<il>Newarr, assembly.MainModule.TypeSystem.Int32\);
                                           \s+\k<il>Dup\);
                                           \s+\k<il>Ldc_I4, 0\);
                                           \s+\k<il>Ldc_I4, 1\);
                                           \s+\k<il>Stelem_I4\);
                                           \s+\k<il>Dup\);
                                           \s+\k<il>Ldc_I4, 1\);
                                           \s+\k<il>Ldc_I4, 2\);
                                           \s+\k<il>Stelem_I4\);
                                           """));
    }
}
