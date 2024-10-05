using System;
using System.IO;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    public class CecilifierUnitTestBase
    {
        protected static CecilifierResult RunCecilifier(string code, INameStrategy nameStrategy = null)
        {
            nameStrategy ??= new DefaultNameStrategy();
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
        
        protected string StructDeclarationRegexFor(string structName, string varName, string accessibilityRegex = ".+")
        {
            return @$"var {varName}_\d+ = new TypeDefinition\("""", ""{structName}"", TypeAttributes.Sealed \|TypeAttributes.AnsiClass \| TypeAttributes.BeforeFieldInit \| TypeAttributes.SequentialLayout \| {accessibilityRegex},.+ImportReference\(typeof\(System.ValueType\)\)\);";
        }
    
        protected string ClassDeclarationRegexFor(string className, string varName, string baseType =".+.TypeSystem.Object", string accessibilityRegex = ".+")
        {
            return @$"var {varName}_\d+ = new TypeDefinition\("""", ""{className}"", TypeAttributes.AnsiClass \| TypeAttributes.BeforeFieldInit \| {accessibilityRegex}, {baseType}\);";
        }
    }
}
