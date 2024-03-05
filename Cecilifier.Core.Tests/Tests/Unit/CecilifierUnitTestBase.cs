using System;
using System.Collections.Generic;
using System.IO;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

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

            try
            {
                return Cecilifier.Process(memoryStream, new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath(), Naming = nameStrategy });
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.ToString());
                throw;
            }
        }
    }
}
