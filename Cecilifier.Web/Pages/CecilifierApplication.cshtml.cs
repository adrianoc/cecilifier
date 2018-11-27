using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cecilifier.Web.Pages
{
    public class CecilifierApplication : PageModel
    {
        private static string xx;
        
        [BindProperty]
        public string CSharpCode { get; set; }


        [BindProperty(SupportsGet =  true)]
        public string CecilifiedCode { get; set; }
        

        public IActionResult OnPost()
        {
            xx = CSharpCode;
            using (var code = new MemoryStream(Encoding.ASCII.GetBytes(CSharpCode)))
            {
                var result = Core.Cecilifier.Process(code, GetTrustedAssembliesPath());
                return Redirect($"/CecilifierApplication/?cecilifiedCode={System.Web.HttpUtility.UrlEncode(result.ReadToEnd())}");
            }
        }

        public void OnGet()
        {
            CSharpCode = System.Web.HttpUtility.HtmlDecode(xx);
        }
      
        private IList<string> GetTrustedAssembliesPath()
        {
            return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
        }
    }
}