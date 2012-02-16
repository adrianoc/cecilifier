using System;
using System.Diagnostics;
using System.Text;

namespace Ceciifier.Core.Tests.Framework
{
	class TestFramework
	{
		public static void Execute(string executable, string args)
		{
			var processInfo = new ProcessStartInfo(executable, args);
			processInfo.CreateNoWindow = true;
			processInfo.RedirectStandardError = true;
			processInfo.RedirectStandardOutput = true;
			processInfo.UseShellExecute = false;
			
			var process = Process.Start(processInfo);


			StringBuilder err = new StringBuilder();
			StringBuilder @out = new StringBuilder();

			process.ErrorDataReceived += (sender, arg) => err.AppendLine(arg.Data);
			process.OutputDataReceived += (sender, arg) => @out.AppendLine(arg.Data);

			process.BeginErrorReadLine();
			process.BeginOutputReadLine();
			
			process.EnableRaisingEvents = true;
			process.WaitForExit();

			if (!string.IsNullOrWhiteSpace(err.ToString()))
			{
				throw new ApplicationException("Error: " + err + "\r\nOuput: " + @out);
			}
		}
	}
}
