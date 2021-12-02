using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Cecilifier.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var configurationBuilder = new ConfigurationBuilder();
            var config = configurationBuilder.AddJsonFile($"appsettings.Production.json", optional: false).Build();

            var host = new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(serverOptions => { })
                        .UseIISIntegration()
                        .UseUrls(config["ApplicationUrl"])
                        .UseConfiguration(config)
                        .UseStartup<Startup>();

                })
                .Build();

            host.Run();
        }
    }
}
