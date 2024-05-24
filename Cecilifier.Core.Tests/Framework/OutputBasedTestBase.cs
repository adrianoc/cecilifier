using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cecilifier.Runtime;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Cecilifier.Core.Tests.Framework;

public class OutputBasedTestBase : CecilifierTestBase
{
    static readonly int NewLineLength = Environment.NewLine.Length;
    protected string CecilifyAndExecute(string code)
    {
        var outputBasedTestFolder = GetTestOutputBaseFolderFor("OutputBasedTests");

        var codeStream = new MemoryStream(Encoding.ASCII.GetBytes(code));
        var cecilifyResult = CecilifyAndExecute(codeStream, outputBasedTestFolder);
        
        VerifyAssembly(cecilifyResult.CecilifiedOutputAssemblyFilePath, null, new CecilifyTestOptions());
        
        var refsToCopy = new List<string>
        {
            typeof(ILParser).Assembly.Location,
            typeof(TypeReference).Assembly.Location,
            typeof(TypeHelpers).Assembly.Location,
        };
        
        CopyFilesNextToGeneratedExecutable(cecilifyResult.CecilifiedOutputAssemblyFilePath, refsToCopy);

        var output = TestFramework.ExecuteWithOutput("dotnet", cecilifyResult.CecilifiedOutputAssemblyFilePath);
        return output.AsSpan()[..^NewLineLength].ToString(); // remove last new line
    }
}
