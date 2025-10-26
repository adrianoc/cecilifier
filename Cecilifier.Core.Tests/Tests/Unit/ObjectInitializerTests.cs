using Cecilifier.Core.Tests.Tests.Unit.Framework;
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

        var expected = """
                       (il_M_\d+\.Emit\(OpCodes\.)Newobj, ctor_foo_3\);
                       (\s+\1)Dup\);
                       \2Ldc_I4, 42\);
                       \2Stfld, fld_value_\d+\);
                       \2Dup\);
                       \2Ldc_I4, 1\);
                       \2Stfld, fld_B_\d+\);
                       \2Stloc, l_x_\d+\);
                       """;

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

        var expected = @"m_topLevelMain_\d+.Body.Variables.Add\((l_x_\d+)\);\s+" +
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
            m_topLevelMain_\d+.Body.Variables.Add\(l_vt_\d+\);
            (\s+il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, (l_vt_\d+)\);
            \1Initobj, st_bar_0\);
            \1Ldloca_S, \2\);
            \1Dup\);
            \1Ldc_I4, 1\);
            \1Call, l_set_\d+\);
            \1Pop\);
            \1Ldloc, \2\);
            \s+var (l_vt_\d+) = new VariableDefinition\(st_bar_0\);
            \s+m_topLevelMain_\d+.Body.Variables.Add\(\3\);
            \1Ldloca_S, \3\);
            \1Initobj, st_bar_0\);
            \1Ldloca_S, \3\);
            \1Dup\);
            \1Ldc_I4, 2\);
            \1Call, l_set_\d+\);
            \1Pop\);
            \1Ldloca, \3\);
            \1Call, m_M_\d+\);
            """,
        TestName = "As method argument")]
    
    [TestCase("""var x = new Bar { Name = "A", Value = 3 };""", 
        """
        var (l_x_\d+) = new VariableDefinition\(st_bar_0\);
        \s+m_topLevelMain_\d+.Body.Variables.Add\(\1\);
        (\s+il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, \1\);
        \2Initobj, st_bar_0\);
        \2Ldloca_S, \1\);
        \2Dup\);
        \2Ldstr, "A"\);
        \2Call, l_set_\d+\);
        \2Dup\);
        \2Ldc_I4, 3\);
        \2Call, l_set_\d+\);
        \2Pop\);
        """,
        TestName = "in variable initializer")]
    
    [TestCase("Bar b; b = new Bar { Value = 3 };", 
        """
        (\s+il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, (l_b_\d+)\);
        \1Dup\);
        \1Initobj, st_bar_0\);
        \1Dup\);
        \1Ldc_I4, 3\);
        \1Call, l_set_\d+\);
        \1Pop\);
        """,
        TestName = "in assignment")]
    
    [TestCase("""var z = new Bar() { Name = "123", Value = 6 }.Value;""", 
        """
                    var (l_vt_\d+) = new VariableDefinition\((st_bar_\d+)\);
                    \s+(m_topLevelMain_\d+).Body.Variables.Add\(\1\);
                    (\s+il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \1\);
                    \4Initobj, \2\);
                    \4Ldloca_S, \1\);
                    \4Dup\);
                    \4Ldstr, "123"\);
                    \4Call, l_set_\d+\);
                    \4Dup\);
                    \4Ldc_I4, 6\);
                    \4Call, l_set_\d+\);
                    \4Pop\);
                    \4Ldloca, \1\);
                    \4Call, m_get_\d+\);
                    """,
        TestName = "in member access expression")]
    
    [TestCase("Bar []ba = new Bar[1]; ba[0] = new Bar { Value = 3 };", 
        """
        \s+//Bar \[\]ba = new Bar\[1\];
        \s+var (?<array_var>l_ba_\d+) = new VariableDefinition\(st_bar_0.MakeArrayType\(\)\);
        \s+m_topLevelMain_\d+.Body.Variables.Add\(\k<array_var>\);
        (\s+il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldc_I4, 1\);
        \1Newarr, st_bar_0\);
        \1Stloc, \k<array_var>\);
        \s+//ba\[0\] = new Bar { Value = 3 };
        \1Ldloc, \k<array_var>\);
        \1Ldc_I4, 0\);
        \1Ldelema, st_bar_0\);
        \1Dup\);
        \1Initobj, st_bar_0\);
        \1Dup\);
        \1Ldc_I4, 3\);
        \1Call, l_set_\d+\);
        \1Pop\);
        """,
        TestName = "in assignment to array element")]    
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
