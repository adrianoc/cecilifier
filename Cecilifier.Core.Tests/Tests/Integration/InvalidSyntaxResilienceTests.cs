using System.IO;
using System.Text;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.Misc;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class InvalidSyntaxResilienceTests
    {
        [Test]
        public void InvalidIdentifier()
        {
            var codeString = "class C { void F(int i) { sitch(i) {} } }";
            using (var code = new MemoryStream(Encoding.ASCII.GetBytes(codeString)))
            {
                Assert.Throws<SyntaxErrorException>(
                    ()=> Cecilifier.Process<MonoCecilContext>(code, new CecilifierOptions { References = MonoCecilContext.BclAssembliesForCompilation() }));
            }
        }
    }
}
