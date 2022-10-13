using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cecilifier.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var configurationBuilder = new ConfigurationBuilder();
            var appsettingsJsonFileName = "appsettings.json";
            if (File.Exists("appsettings.Production.json"))
            {
                appsettingsJsonFileName = "appsettings.Production.json";
            }
            var config = configurationBuilder.AddJsonFile(appsettingsJsonFileName, optional: false).Build();

            var host = new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(serverOptions => { })
                        .UseIISIntegration()
                        .UseUrls(config["ApplicationUrl"])
                        .UseConfiguration(config)
                        .UseStartup<Startup>()
                        .ConfigureLogging(logBuilder => logBuilder.AddConsole());

                })
                .Build();

            host.Run();
        }
    }
}
