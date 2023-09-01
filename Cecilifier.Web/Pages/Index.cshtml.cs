using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Cecilifier.Web.Pages
{
    public class CecilifierApplication : PageModel
    {
        public static int Count;
        public static uint UniqueClients; 
        public static uint MaximumUnique; 

        public static int SupportedCSharpVersion => Core.Cecilifier.SupportedCSharpVersion;
        public string FromGist { get; private set; } = string.Empty;
        public string ErrorAccessingGist { get; private set; } = string.Empty;

        public CecilifierApplication(ILogger<CecilifierApplication> logger)
        {
            // Gambi. I have no idea how to get access inside the rest handler.
            //        Most likely there's a way to ask DI go give us one. 
            CecilifierRestHandler._logger = logger;
        }

        public async Task<IActionResult> OnGet()
        {
            ErrorAccessingGist = null;
            if (Request.Query.TryGetValue("gistid", out var gistid))
            {
                Count++;
                var gistHttp = new HttpClient();
                gistHttp.DefaultRequestHeaders.Add("User-Agent", "Cecilifier");
                var result = await gistHttp.GetAsync($"https://api.github.com/gists/{gistid}");

                if (result.StatusCode == HttpStatusCode.OK)
                {
                    var root = JObject.Parse(await result.Content.ReadAsStringAsync());
                    var source = root["files"].First().Children()["content"].FirstOrDefault().ToString();

                    FromGist = Encode(source);
                }
                else
                {
                    ErrorAccessingGist = Encode($"Error accessing GistId = {gistid}: {result.StatusCode}\\n{result.RequestMessage}");
                }
            }

            return Page();

            string Encode(string msg)
            {
                return msg.Replace("\n", @"\n").Replace("\t", @"\t");
            }
        }
    }
}
