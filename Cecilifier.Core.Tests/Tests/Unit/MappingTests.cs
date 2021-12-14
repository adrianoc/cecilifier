using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class MappingTests
    {
        [Test]
        public void Test_CecilifierPreamble_Has17Lines()
        {
            var cecilifiedResult = RunCecilifier("class Foo {}");
            Assert.That(
                cecilifiedResult.Mappings[0].Cecilified.Begin.Line, 
                Is.EqualTo(Cecilifier.CecilifierProgramPreambleLength), 
                $"If this test ever fail check {nameof(CecilifierExtensions.AsCecilApplication)}(). Most likely the preamble appended to the cecilified code has changed.");
        }

        [Test]
        public void Test_ClassAndMethod_InSingleLine()
        {
            //                                                  1         2         3         4         5
            //                                         12345678901234567890123456789012345678901234567890
            var cecilifiedResult = RunCecilifier("class Foo { int Sum(int i, int j) => i + j; }");
            Assert.That(cecilifiedResult.Mappings.Count, Is.EqualTo(4), $"Actual Mapping:{Environment.NewLine}{cecilifiedResult.Mappings.Dump()}");
            
            // => i + j;
            Assert.That(cecilifiedResult.Mappings[1].Source.Begin.Line, Is.EqualTo(1));
            Assert.That(cecilifiedResult.Mappings[1].Source.Begin.Column, Is.EqualTo(35));
            
            // => int Sum(int i, int j) => i + j;
            Assert.That(cecilifiedResult.Mappings[2].Source.Begin.Line, Is.EqualTo(1));
            Assert.That(cecilifiedResult.Mappings[2].Source.Begin.Column, Is.EqualTo(13));
            Assert.That(cecilifiedResult.Mappings[2].Source.End.Column, Is.EqualTo(44));
            
            // Whole class
            Assert.That(cecilifiedResult.Mappings[3].Source.Begin.Line, Is.EqualTo(1));
            Assert.That(cecilifiedResult.Mappings[3].Source.Begin.Column, Is.EqualTo(1));
            Assert.That(cecilifiedResult.Mappings[3].Source.End.Column, Is.EqualTo(46)); // we add a new line at the end of the cecilified code.
        }

        private static CecilifierResult RunCecilifier(string code)
        {
            var nameStrategy = new DefaultNameStrategy();
            var memoryStream = new MemoryStream();
            memoryStream.Write(System.Text.Encoding.ASCII.GetBytes(code));
            memoryStream.Position = 0;

            return Cecilifier.Process(memoryStream, new CecilifierOptions { References = Utils.GetTrustedAssembliesPath(), Naming = nameStrategy });
        }
    }

    internal static class MappingsExtensions
    {
        internal static string Dump(this IList<Mappings.Mapping> self)
        {
            return string.Join(System.Environment.NewLine, self.Select(s => s.ToString()));
        }
    }
}
