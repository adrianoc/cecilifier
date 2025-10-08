using System;
using System.IO;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture(typeof(MonoCecilContext), TestName = "Mono.Cecil")]
    [TestFixture(typeof(SystemReflectionMetadataContext),  TestName = "System.Reflection.Metadata")]
    [EnableForContext<SystemReflectionMetadataContext>(nameof(Test_CecilifierPreamble_LineCount), IgnoreReason = "Not implemented")]
    public class MappingTests<TContext> where TContext : IVisitorContext
    {
        [Test]
        public void Test_CecilifierPreamble_LineCount()
        {
            var result = RunCecilifier("class Foo {}");
            
            Assert.That(
                result.Mappings[0].Cecilified.Begin.Line,
                Is.EqualTo(result.Context.ApiDriver.PreambleLineCount),
                $"If this test ever fail check the method {result.Context.ApiDriver.GetType().Name}.AsCecilApplication(). Most likely property `{result.Context.ApiDriver.GetType().Name}.PreambleLineCount` does not match the first line appended after the preamble.");
        }

        [Test]
        public void Test_ClassAndMethod_InSingleLine()
        {
            //                                                  1         2         3         4         5
            //                                         12345678901234567890123456789012345678901234567890
            var result = RunCecilifier("class Foo { int Sum(int i, int j) => i + j; }");
            var message = $"Actual Mapping:{Environment.NewLine}{result.Mappings.DumpAsString()}\n\n{result.GeneratedCode.ReadToEnd()}";
            
            Assert.That(result.Mappings.Count, Is.EqualTo(9), message);

            // Whole class
            Assert.That(result.Mappings[0].Source.Begin.Line, Is.EqualTo(1), message);
            Assert.That(result.Mappings[0].Source.Begin.Column, Is.EqualTo(1), message);
            Assert.That(result.Mappings[0].Source.End.Column, Is.EqualTo(46), message);

            Assert.That(result.Mappings[0].Cecilified.Begin.Line, Is.EqualTo(25), message);
            Assert.That(result.Mappings[0].Cecilified.End.Line, Is.EqualTo(55), message);

            // => int Sum(int i, int j) => i + j;
            Assert.That(result.Mappings[1].Source.Begin.Line, Is.EqualTo(1), message);
            Assert.That(result.Mappings[1].Source.Begin.Column, Is.EqualTo(13), message);
            Assert.That(result.Mappings[1].Source.End.Column, Is.EqualTo(44), message);

            Assert.That(result.Mappings[1].Cecilified.Begin.Line, Is.EqualTo(30), message);
            Assert.That(result.Mappings[1].Cecilified.End.Line, Is.EqualTo(47), message);
            
            // parameter i
            Assert.That(result.Mappings[2].Source.Begin.Line, Is.EqualTo(1), message);
            Assert.That(result.Mappings[2].Source.Begin.Column, Is.EqualTo(21), message);
            Assert.That(result.Mappings[2].Source.End.Column, Is.EqualTo(26), message);

            Assert.That(result.Mappings[2].Cecilified.Begin.Line, Is.EqualTo(38), message);
            Assert.That(result.Mappings[2].Cecilified.End.Line, Is.EqualTo(40), message);
            
            // parameter j
            Assert.That(result.Mappings[3].Source.Begin.Line, Is.EqualTo(1), message);
            Assert.That(result.Mappings[3].Source.Begin.Column, Is.EqualTo(28), message);
            Assert.That(result.Mappings[3].Source.End.Column, Is.EqualTo(33), message);

            Assert.That(result.Mappings[3].Cecilified.Begin.Line, Is.EqualTo(40), message);
            Assert.That(result.Mappings[3].Cecilified.End.Line, Is.EqualTo(42), message);
            
            // => i + j;
            Assert.That(result.Mappings[4].Source.Begin.Line, Is.EqualTo(1), message);
            Assert.That(result.Mappings[4].Source.Begin.Column, Is.EqualTo(35), message);

            Assert.That(result.Mappings[4].Cecilified.Begin.Line, Is.EqualTo(42), message);
            Assert.That(result.Mappings[4].Cecilified.End.Line, Is.EqualTo(46), message);
        }

        [Test]
        public void Test_Content_Matches_ReportedCecilifiedMappings()
        {
            var result = RunCecilifier("class Foo { int Sum(int i, int j) => i + j; }");
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            var message = $"Actual Mapping:{Environment.NewLine}{result.Mappings.DumpAsString()}\n\n{cecilifiedCode}";

            var cecilifiedLines = cecilifiedCode.Split(Environment.NewLine);
            
            // Whole class
            Assert.That(cecilifiedLines[result.Mappings[0].Cecilified.Begin.Line], Contains.Substring("//Class : Foo"), message);
            
            // Method Sum()...
            Assert.That(cecilifiedLines[result.Mappings[1].Cecilified.Begin.Line], Contains.Substring("//Method : Sum"), message);
        }

        private static CecilifierResult RunCecilifier(string code)
        {
            var memoryStream = new MemoryStream();
            memoryStream.Write(System.Text.Encoding.ASCII.GetBytes(code));
            memoryStream.Position = 0;

            var options = new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath(), Naming = new DefaultNameStrategy()};
            return  Cecilifier.Process<TContext>(memoryStream, options);
        }
    }
}
