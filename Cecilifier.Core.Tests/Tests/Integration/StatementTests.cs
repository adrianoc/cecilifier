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
        
        [Test]
        public void TestForStatement()
        {    
            AssertResourceTestWithExplicitExpectation(@"Statements/ForStatement", "System.Int32 ForStatement::M()");
        }
        
        [Test]
        public void TestSwitchStatement()
        {    
            AssertResourceTestWithExplicitExpectation(@"Statements/SwitchStatement", "System.Int32 SwitchStatement::M(System.Int32)");
        }
        
        [Test]
        public void TestUsingWithStructExpression()
        {    
            AssertResourceTestWithExplicitExpectation(@"Statements/UsingStatement.StructExpression", "System.Void UsingStatementTest::WithStructExpression()");
        }
        
        [Test]
        public void TestUsingStatement()
        {    
            AssertResourceTest(@"Statements/UsingStatement");
        }
    }
}
