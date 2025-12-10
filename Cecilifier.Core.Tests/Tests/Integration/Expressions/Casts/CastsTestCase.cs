using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Casts
{
    [TestFixture(typeof(MonoCecilContext), TestName = "Mono.Cecil")]
    [TestFixture(typeof(SystemReflectionMetadataContext), TestName = "System.Reflection.Metadata")]
    public class CastsTestCase<TContext> : ResourceTestBase<TContext> where TContext : IVisitorContext
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
        [ParameterizedResourceFilter<SystemReflectionMetadataContext>(IgnoreReason = "Generic types are not supported as of today on SRM")]
        public void TestGenericTypeParameter([Values("object", "T")] string source, [Values("object", "T")] string target)
        {
            AssertResourceTestWithParameters("Expressions/Casts/GenericTypeCasts", source, target);
        }

        [Test]
        [ParameterizedResourceFilter<SystemReflectionMetadataContext>(IgnoreReason = "Generic types are not supported as of today on SRM")]
        public void TestGenerics([Values("Base<int>", "Derived")] string source, [Values("Base<int>", "Derived")] string target)
        {
            AssertResourceTestWithParameters("Expressions/Casts/GenericTypeCasts", source, target);
        }
    }
}
