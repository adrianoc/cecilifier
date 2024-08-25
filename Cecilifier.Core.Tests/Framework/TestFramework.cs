using System;
using System.Diagnostics;
using System.Text;

namespace Cecilifier.Core.Tests.Framework
{
    internal class TestFramework
    {
        public static void Execute(string executable, string args)
        {
            var output = ExecuteWithOutput(executable, args);
            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"{Environment.NewLine}Output: {output}");
            }
        }
        
        public static string ExecuteWithOutput(string executable, string args)
        {
            var processInfo = new ProcessStartInfo(executable, args);
            processInfo.CreateNoWindow = true;
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.UseShellExecute = false;

            using var process = Process.Start(processInfo);

            var err = new StringBuilder();
            var @out = new StringBuilder();

            process.ErrorDataReceived += (sender, arg) =>
            {
                if (!string.IsNullOrWhiteSpace(arg.Data)) 
                    err.AppendLine(arg.Data);
            };
            
            process.OutputDataReceived += (sender, arg) =>
            {
                if (!string.IsNullOrWhiteSpace(arg.Data)) 
                    @out.AppendLine(arg.Data);
            };

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            process.EnableRaisingEvents = true;
            process.WaitForExit();

            if (err.Length == 0)
                return @out.ToString();
                
            if (@out.Length > 0)
            {
                Console.WriteLine($"{Environment.NewLine}Output: {@out}");
            }

            throw new ApplicationException("Error: " + err + $"{Environment.NewLine}Executable: {executable}");
        }
    }
}
