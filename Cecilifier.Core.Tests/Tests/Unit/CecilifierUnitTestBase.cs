using System.IO;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Tests.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    public class CecilifierUnitTestBase
    {
        protected static CecilifierResult RunCecilifier(string code)
        {
            var nameStrategy = new DefaultNameStrategy();
            var memoryStream = new MemoryStream();
            memoryStream.Write(System.Text.Encoding.ASCII.GetBytes(code));
            memoryStream.Position = 0;

            return Cecilifier.Process(memoryStream, new CecilifierOptions { References = Utils.GetTrustedAssembliesPath(), Naming = nameStrategy });
        }
    }
}
