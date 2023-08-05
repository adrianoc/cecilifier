using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ObjectInitializerTests : CecilifierUnitTestBase
{
    // https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/object-and-collection-initializers
    [Test]
    public void ObjectInitializer()
    {
        var code = @"class Foo { public int Value; public bool B; } class Bar { public void M() { var x = new Foo { Value = 42, B = true }; } }";
        var result = RunCecilifier(code);

        var expected = @"(il_M_\d+\.Emit\(OpCodes\.)Newobj, ctor_foo_3\);\s+" +
            @"var (dup_\d+) = il_M_\d+.Create\(OpCodes.Dup\);\s+" +
            @"il_M_\d+.Append\(\2\);\s+" +
            @"\1Ldc_I4, 42\);\s+" +
            @"\1Stfld, fld_value_\d+\);\s+" +
            @"var (dup_\d+) = il_M_\d+.Create\(OpCodes.Dup\);\s+" +
            @"il_M_\d+.Append\(\3\);\s+" +
            @"\1Ldc_I4, 1\);\s+" +
            @"\1Stfld, fld_B_\d+\);\s+" +
            @"\1Stloc, l_x_\d+\);";

        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }

    [Test]
    public void DictionaryInitializer()
    {
        var code = @"var x = new System.Collections.Generic.Dictionary<int, int>() 
                    {
                        [1] = 11, [2] = 22
                    };";

        var result = RunCecilifier(code);

        var expected = @"m_topLevelStatements_1.Body.Variables.Add\((l_x_\d+)\);\s+" +
                            @"(il_topLevelMain_\d+\.Emit\(OpCodes\.)Newobj,.+typeof\(System\.Collections\.Generic\.Dictionary<System.Int32, System.Int32>\).+\)\);\s+" +
                            @"\2Dup\);\s+" +
                            @"\2Ldc_I4, 1\);\s+" +
                            @"\2Ldc_I4, 11\);\s+" +
                            @"\2Callvirt,.+set_Item.+\)\);\s+" +
                            @"\2Dup\);\s+" +
                            @"\2Ldc_I4, 2\);\s+" +
                            @"\2Ldc_I4, 22\);\s+" +
                            @"\2Callvirt,.+set_Item.+\)\);\s+" +
                            @"\2Stloc, \1\);\s+";

        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }

    [Test]
    public void CustomCollectionAndComplexObjectInitializer()
    {
        var code = @"using System.Collections;
                    
                    class CollectionFoo : IEnumerable 
                    { 
                        public IEnumerator GetEnumerator() { return null;}
                        public void Add(bool b, int i) {}
                        public void Add(char ch) {} 
                    }

                    class Driver
                    {
                        void M()
                        {
                            var x = new CollectionFoo()
                                                {
                                                    {true, 1},
                                                    {'A'}
                                                };
 
                        }
                    }";

        var result = RunCecilifier(code);

        var expected = @"(il_M_\d+.Emit\(OpCodes\.)Newobj, ctor_collectionFoo_\d+\);\s+" +
                       @"\1Dup\);\s+" +
                       @"\1Ldc_I4, 1\);\s+" +
                       @"\1Ldc_I4, 1\);\s+" +
                       @"\1Callvirt,.+m_add_\d+\);\s+" +
                       @"\1Dup\);\s+" +
                       @"\1Ldc_I4, 65\);\s+" +
                       @"\1Callvirt,.+m_add_\d+\);\s+";

        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }

    [Test]
    public void CustomCollectionInitializer()
    {
        var code = """
                   using System.Collections;
                   class CollectionFoo : IEnumerable 
                   {
                        public IEnumerator GetEnumerator() => null; 
                        public void Add(char ch) {} 
                   }

                   class Driver { CollectionFoo M() => new CollectionFoo() { 'A', 'B', 'C' }; }
                   """;

        var result = RunCecilifier(code);

        var expected = @"(il_M_\d+.Emit\(OpCodes\.)Newobj, ctor_collectionFoo_\d+\);\s+" +
                       @"\1Dup\);\s+" +
                       @"\1Ldc_I4, 65\);\s+" +
                       @"\1Callvirt,.+m_add_\d+\);\s+" +
                       @"\1Dup\);\s+" +
                       @"\1Ldc_I4, 66\);\s+" +
                       @"\1Callvirt,.+m_add_\d+\);\s+"+
                       @"\1Dup\);\s+" +
                       @"\1Ldc_I4, 67\);\s+" +
                       @"\1Callvirt,.+m_add_\d+\);";

        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }
    
    [Test]
    public void CustomCollectionInitializerAddMethodBoundToExtensionMethod()
    {
        var code = """
                   static class Ext { public static void Add(this Foo self, string s) {} }

                   class Foo : System.Collections.IEnumerable
                   {
                        public System.Collections.IEnumerator GetEnumerator() => null;
                        
                        static void Main() 
                        {
                            var f  = new Foo() { "W1",  "W2" };
                        }
                   }
                   """;

        var result = RunCecilifier(code);

        var expected = @"(il_main_\d+\.Emit\(OpCodes\.)Dup\);\s+" +
                       @"\1Ldstr, ""W1""\);\s+" +
                       @"\1Call,.+m_add_\d+\);\s+"+
                       @"\1Dup\);\s+" +
                       @"\1Ldstr, ""W2""\);\s+" +
                       @"\1Call,.+m_add_\d+\);";

        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }

    [TestCase(
        "M(new Bar() { Value = 1 } , new Bar() { Value = 2 } );",
        """
            m_topLevelStatements_\d+.Body.Variables.Add\(l_vt_\d+\);
            (\s+il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, (l_vt_\d+)\);
            \1Initobj, st_bar_0\);
            \1Ldloca_S, \2\);
            \s+var dup_23 = il_topLevelMain_\d+.Create\(OpCodes.Dup\);
            \s+il_topLevelMain_\d+.Append\(dup_23\);
            \1Ldc_I4, 1\);
            \1Call, l_set_11\);
            \1Pop\);
            \1Ldloc, \2\);
            \s+var (l_vt_\d+) = new VariableDefinition\(st_bar_0\);
            \s+m_topLevelStatements_\d+.Body.Variables.Add\(\3\);
            \1Ldloca_S, \3\);
            \1Initobj, st_bar_0\);
            \1Ldloca_S, \3\);
            \s+var dup_25 = il_topLevelMain_\d+.Create\(OpCodes.Dup\);
            \s+il_topLevelMain_\d+.Append\(dup_25\);
            \1Ldc_I4, 2\);
            \1Call, l_set_11\);
            \1Pop\);
            \1Ldloca, \3\);
            \1Call, m_M_19\);
            """,
        TestName = "As method argument")]
    
    [TestCase("""var x = new Bar { Name = "A", Value = 3 };""", 
        """
        var (l_x_\d+) = new VariableDefinition\(st_bar_0\);
        \s+m_topLevelStatements_14.Body.Variables.Add\(\1\);
        (\s+il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, \1\);
        \2Initobj, st_bar_0\);
        \2Ldloca_S, \1\);
        \s+var dup_20 = il_topLevelMain_16.Create\(OpCodes.Dup\);
        \s+il_topLevelMain_16.Append\(dup_20\);
        \2Ldstr, "A"\);
        \2Call, l_set_5\);
        \s+var dup_21 = il_topLevelMain_16.Create\(OpCodes.Dup\);
        \s+il_topLevelMain_16.Append\(dup_21\);
        \2Ldc_I4, 3\);
        \2Call, l_set_11\);
        \2Pop\);
        """,
        TestName = "in variable initializer")]
    
    [TestCase("Bar b; b = new Bar { Value = 3 };", 
        """
        (\s+il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, (l_b_\d+)\);
        \1Dup\);
        \1Initobj, st_bar_0\);
        \s+var dup_20 = il_topLevelMain_16.Create\(OpCodes.Dup\);
        \s+il_topLevelMain_16.Append\(dup_20\);
        \1Ldc_I4, 3\);
        \1Call, l_set_11\);
        \1Pop\);
        """,
        TestName = "in assignment")]
    
    [TestCase("""var z = new Bar() { Name = "123", Value = 6 }.Value;""", 
        """
                    var (l_vt_\d+) = new VariableDefinition\((st_bar_\d+)\);
                    \s+(m_topLevelStatements_\d+).Body.Variables.Add\(\1\);
                    (\s+il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \1\);
                    \4Initobj, \2\);
                    \4Ldloca_S, \1\);
                    \s+var dup_\d+ = il_topLevelMain_\d+.Create\(OpCodes.Dup\);
                    \s+il_topLevelMain_\d+.Append\(dup_\d+\);
                    \4Ldstr, "123"\);
                    \4Call, l_set_5\);
                    \s+var dup_\d+ = il_topLevelMain_\d+.Create\(OpCodes.Dup\);
                    \s+il_topLevelMain_\d+.Append\(dup_\d+\);
                    \4Ldc_I4, 6\);
                    \4Call, l_set_11\);
                    \4Pop\);
                    \4Ldloca, \1\);
                    \4Call, m_get_8\);
                    """,
        TestName = "in member access expression")]
    public void ObjectInitializers_AreHandled_InValueTypes(string statementToTest, string expectedRegex)
    {
        var result = RunCecilifier($$"""
                                   {{statementToTest}}
                                   
                                   void M(Bar bar, in Bar ib) { }
                                   
                                   struct Bar 
                                   {
                                      public string Name {get; set; }
                                      public int Value { get; set; }
                                   }
                                   """);
        
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedRegex));
    }
    
}
