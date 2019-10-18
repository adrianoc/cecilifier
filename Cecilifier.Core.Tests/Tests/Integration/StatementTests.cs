using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class StatementTests : IntegrationResourceBasedTest
    {
        [Test]
        public void TestFixedStatement()
        {    
            AssertResourceTestWithExplicitExpectation(@"Statements/FixedStatement", "System.Int32* FixedStatementTest::FixedStatement()");
        }
    }
}
