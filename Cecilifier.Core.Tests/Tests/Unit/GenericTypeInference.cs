using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class GenericTypeInference : CecilifierUnitTestBase
    {
        [Test]
        public void ExplicitType()
        {
            var code = "class Foo { void M<T>() {} void Explicit() { M<int>(); }  }";
            var expectedSnippet = @"var gi_M_8 = new GenericInstanceMethod\(r_M_7\).+\s+" + 
                                       @"gi_M_8.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);\s+";
            
            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }
        
        [Test]
        public void InferredType()
        {
            var code = "class Foo { void M<T>(T t) {} void Inferred() { M(10); }  }";
            var expectedSnippet = @"var gi_M_9 = new GenericInstanceMethod\(r_M_8\).+\s+" + 
                                  @"gi_M_9.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);\s+";

            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }
    }
}
