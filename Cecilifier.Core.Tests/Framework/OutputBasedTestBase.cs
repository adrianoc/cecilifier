using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cecilifier.Runtime;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Framework;

public record struct OutputBasedTestResult(CecilifyResult GeneralResult, string Output);

public class OutputBasedTestBase : CecilifierTestBase
{
    static readonly int NewLineLength = Environment.NewLine.Length;
    
    protected OutputBasedTestResult CecilifyAndExecute(string code)
    {
        var outputBasedTestFolder = GetTestOutputBaseFolderFor("OutputBasedTests");

        var cecilifyResult = CecilifyAndExecute(new MemoryStream(Encoding.ASCII.GetBytes(code)), outputBasedTestFolder);
        
        VerifyAssembly(cecilifyResult.CecilifiedOutputAssemblyFilePath, null, new CecilifyTestOptions { CecilifiedCode = cecilifyResult.CecilifiedCode });
        
        var refsToCopy = new List<string>
        {
            typeof(ILParser).Assembly.Location,
            typeof(TypeReference).Assembly.Location,
            typeof(TypeHelpers).Assembly.Location,
        };
        
        CopyFilesNextToGeneratedExecutable(cecilifyResult.CecilifiedOutputAssemblyFilePath, refsToCopy);

        var output = TestFramework.ExecuteWithOutput("dotnet", cecilifyResult.CecilifiedOutputAssemblyFilePath);
        return new OutputBasedTestResult(cecilifyResult, output.AsSpan()[..^NewLineLength].ToString()); // remove last new line
    }


    protected void AssertOutput(string snippet, string expectedOutput)
    {
        var result = CecilifyAndExecute(snippet);
        Assert.That(result.Output, Is.EqualTo(expectedOutput), $"Output Assembly: {result.GeneralResult.CecilifiedOutputAssemblyFilePath}");
        TestContext.WriteLine($"Output Assembly: {result.GeneralResult.CecilifiedOutputAssemblyFilePath}");
    }
}
