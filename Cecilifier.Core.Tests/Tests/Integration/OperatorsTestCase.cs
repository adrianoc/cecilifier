using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    public class Operators<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [Test]
        public void BitwiseOperators([Values("Or", "And", "Xor", "Shift")] string @operator, [Values("int", "char", "byte", "long", "sbyte")] string type1, [Values("int", "char", "byte", "long", "sbyte")] string type2)
        {
            AssertResourceTestWithParameters(@$"Expressions/Operators/Bitwise/Bitwise{@operator}", type1, type2);
        }

        [Test]
        public void LogicalOperators([Values("Or", "And")] string @operator)
        {
            AssertResourceTest(@$"Expressions/Operators/Logical/Logical{@operator}");
        }
    }
}
