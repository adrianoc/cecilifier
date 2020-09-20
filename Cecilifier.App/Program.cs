using System;
using System.IO;

namespace Cecilifier.App
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                //TODO: validate the arguments correctly
                Console.Error.WriteLine("Missing source file path.");
                return 1;
            }

            try
            {
                var references = File.ReadAllLines(args[1]);
                using var toBeCecilified = File.OpenRead(args[0]);
                var result = Core.Cecilifier.Process(toBeCecilified, references);

                File.WriteAllText(args[0], result.GeneratedCode.ReadToEnd());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Exception: {ex}");
            }

            return 1;
        }
    }
}
