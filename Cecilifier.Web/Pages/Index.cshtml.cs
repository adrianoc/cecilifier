using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json.Linq;

namespace Cecilifier.Web.Pages
{
    public class CecilifierApplication : PageModel
    {
        public static int Count;
        
        public string FromGist { get; set; } = string.Empty;
        public string ErrorAccessingGist { get; private set; } = string.Empty;
        
        public async void OnGet()
        {
            ErrorAccessingGist = null;
            if (Request.Query.TryGetValue("gistid", out var gistid))
            {
                Count++;
                var gistHttp = new HttpClient();
                gistHttp.DefaultRequestHeaders.Add("User-Agent", "Cecilifier");
                var task = gistHttp.GetAsync($"https://api.github.com/gists/{gistid}");
                await task;
                
                if (task.Result.StatusCode == HttpStatusCode.OK)
                {
                    var root = JObject.Parse(await task.Result.Content.ReadAsStringAsync());
                    var source = root["files"].First().Children()["content"].FirstOrDefault().ToString();

                    FromGist = Encode(source);
                }
                else
                {
                    ErrorAccessingGist = Encode($"Error accessing GistId = {gistid}: {task.Result.StatusCode}\\n{task.Result.RequestMessage}");
                }
            }

            string Encode(string msg)
            {                
                return msg.Replace("\n", @"\n").Replace("\t", @"\t");
            }
        }
    }
}
