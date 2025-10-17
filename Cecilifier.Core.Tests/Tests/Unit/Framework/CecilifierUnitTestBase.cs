using System;
using System.IO;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit.Framework
{
    public class CecilifierUnitTestBase
    {
        protected static CecilifierResult RunCecilifier(string code, INameStrategy nameStrategy = null) => RunCecilifier<MonoCecilContext>(code, nameStrategy);

        protected static CecilifierResult RunCecilifier<TContext>(string code, INameStrategy nameStrategy = null) where TContext : IVisitorContext
        {
            nameStrategy ??= new DefaultNameStrategy();
            var memoryStream = new MemoryStream();
            memoryStream.Write(System.Text.Encoding.ASCII.GetBytes(code));
            memoryStream.Position = 0;

            try
            {
                return Cecilifier.Process<TContext>(
                    memoryStream,
                    new CecilifierOptions { References = TContext.BclAssembliesForCompilation(), Naming = nameStrategy });
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
