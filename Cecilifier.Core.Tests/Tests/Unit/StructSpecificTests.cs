using Microsoft.VisualStudio.TestPlatform.CrossPlatEngine;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class StructSpecificTests : CecilifierUnitTestBase
{
    [Test]
    public void ReadOnlyStructDeclaration()
    {
        var result = RunCecilifier("readonly struct RO { }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(@$"st_rO_\d+\.CustomAttributes\.Add\(new CustomAttribute\(.+typeof\(System.Runtime.CompilerServices.IsReadOnlyAttribute\), "".ctor"".+\)\);"));
    }

    [TestCase("using System.Runtime.InteropServices; [StructLayout(LayoutKind.Auto, Size = 4)] struct S {}", "AutoLayout", TestName = "AutoLayout")]
    [TestCase("using System.Runtime.InteropServices; [StructLayout(LayoutKind.Explicit, Size = 42)] struct S {}", "ExplicitLayout", TestName = "ExplicitLayout")]
    [TestCase("struct S {}", "SequentialLayout", TestName = "DefaultLayout")]
    public void StructLayoutAttributeIsAdded(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(@$"TypeAttributes\.{expected}"));
    }

    [Test]
    public void RefStructDeclaration()
    {
        var result = RunCecilifier("ref struct RS { }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(@"st_rS_\d+\.CustomAttributes\.Add\(new CustomAttribute\(.+typeof\(System.Runtime.CompilerServices.IsByRefLikeAttribute\), "".ctor"".+\)\);"));
        Assert.That(cecilifiedCode, Does.Match(@"attr_obsolete_\d+\.ConstructorArguments\.Add\(new CustomAttributeArgument\(.+Boolean, true\)\);"));
        Assert.That(cecilifiedCode, Does.Match(@"st_rS_\d+\.CustomAttributes\.Add\(attr_obsolete_\d+\);"));
    }
    
    [TestCase(
        "parameter",
   """
               //l = parameter;
               (.+il_M_\d+\.Emit\(OpCodes\.)Ldarg_1\);
               \1Box, (st_test_\d+)\);
               \1Stloc, l_l_\d+\);
               
               .+//return parameter;
               \1Ldarg_1\);
               \1Box, \2\);
               \1Ret\);
               """,
        TestName = "Parameter")]
    
    [TestCase(
        "field",
   """
               //l = field;
               (.+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
               \1Ldfld, (fld_field_\d+)\);
               \1Box, (st_test_\d+)\);
               \1Stloc, l_l_\d+\);
               
               .+//return field;
               \1Ldarg_0\);
               \1Ldfld, \2\);
               \1Box, \3\);
               \1Ret\);
               """,
        TestName = "Field")]
    
    [TestCase(
        "local",
   """
               //l = local;
               (.+il_M_\d+\.Emit\(OpCodes\.)Ldloc, (l_local_\d+)\);
               \1Box, (st_test_\d+)\);
               \1Stloc, l_l_\d+\);
               
               .+//return local;
               \1Ldloc, \2\);
               \1Box, \3\);
               \1Ret\);
               """,
        TestName = "Local")]
    public void AssignmentToInterfaceTypedVariable(string member, string expectedRegex)
    {
        var result = RunCecilifier(
            $$"""
                struct Test : System.IDisposable { public void Dispose() {} }              
                class D
                {
                     Test field;
                     System.IDisposable M(Test parameter)
                     {
                         Test local = parameter;
                         System.IDisposable l;
                         l = {{member}};

                         return {{member}};
                     }
                }
                """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(expectedRegex));
    }
}
