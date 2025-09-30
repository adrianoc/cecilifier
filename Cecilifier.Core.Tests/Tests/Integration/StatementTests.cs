using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    [TestFixture(typeof(SystemReflectionMetadataContext))]
    [EnableForContext<SystemReflectionMetadataContext>(nameof(TestForStatement))]
    public class StatementTests<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [TestCase("System.Int32* FixedStatementTest::Test()", TestName = "Return")]
        [TestCase("System.Void FixedStatementTest::Test()", TestName = "Local")]
        public void TestFixedStatement(string methodToVerify)
        {
            var options = new CecilifyTestOptions()
            {
                ResourceName = $"Statements/FixedStatement{TestContext.CurrentContext.Test.Name}",
                IgnoredILErrors = "ExpectedNumericType" // looks like harmless (not sure why ilverify complains)
            };

            AssertResourceTestWithExplicitExpectation(options, methodToVerify);
        }

        [Test]
        public void TestForStatement()
        {
            AssertResourceTestWithExplicitExpectation("Statements/ForStatement", "System.Int32 ForStatement::M()");
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
