using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Casts
{
    [TestFixture]
    public class CastsTestCase : IntegrationResourceBasedTest
    {
        [Test]
        public void TestPrimitiveNumericCasts([Values("int", "long", "double", "short", "byte", "char")] string source, [Values("int", "long", "double", "short", "byte", "char")] string target)
        {
            AssertResourceTestWithParameters("Expressions/Casts/PrimitiveNumericCasts", source, target);
        }

        [Test]
        public void TestReferenceCasts([Values("object", "string")] string source, [Values("object", "string")] string target)
        {
            AssertResourceTestWithParameters("Expressions/Casts/ReferenceCasts", source, target);
        }

        [Test]
        public void TestReferenceCastsWithTypesFromSameModule([Values("Base", "Derived", "object")] string source, [Values("Base", "Derived", "object")] string target)
        {
            AssertResourceTestWithParameters("Expressions/Casts/ReferenceCasts", source, target);
        }

        [Test]
        public void TestGenericTypeParameter([Values("object", "T")] string source, [Values("object", "T")] string target)
        {
            AssertResourceTestWithParameters("Expressions/Casts/GenericTypeCasts", source, target);
        }

        [Test]
        public void TestGenerics([Values("Base<int>", "Derived")] string source, [Values("Base<int>", "Derived")] string target)
        {
            AssertResourceTestWithParameters("Expressions/Casts/GenericTypeCasts", source, target);
        }
    }
}
