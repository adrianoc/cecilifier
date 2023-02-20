using System;
using System.Diagnostics;
using System.Text;

namespace Cecilifier.Core.Tests.Framework
{
    internal class TestFramework
    {
        public static void Execute(string executable, string args)
        {
            var processInfo = new ProcessStartInfo(executable, args);
            processInfo.CreateNoWindow = true;
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.UseShellExecute = false;

            var process = Process.Start(processInfo);

            var err = new StringBuilder();
            var @out = new StringBuilder();

            process.ErrorDataReceived += (sender, arg) => err.AppendLine(arg.Data);
            process.OutputDataReceived += (sender, arg) => @out.AppendLine(arg.Data);

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            process.EnableRaisingEvents = true;
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(@out.ToString()))
            {
                Console.WriteLine($"{Environment.NewLine}Output: {@out}");
            }

            if (!string.IsNullOrWhiteSpace(err.ToString()))
            {
                throw new ApplicationException("Error: " + err + $"{Environment.NewLine}Executable: {executable}");
            }
        }
    }
}
