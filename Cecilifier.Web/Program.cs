using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Cecilifier.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(serverOptions => { })
                        .UseIISIntegration()
                        .UseUrls("http://0.0.0.0:8081")
                        .UseStartup<Startup>();
                })
                .Build();
            
            host.Run();
        }
    }
}
