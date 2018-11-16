using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cecilifier.Web.Pages
{
    public class CecilifierApplication : PageModel
    {
        [BindProperty]
        public string CSharpCode { get; set; }
        
        [BindProperty(SupportsGet =  true)]
        public string CecilifiedCode { get; set; }
        

        public async Task<IActionResult> OnPostAsync()
        {
            using (var code = new MemoryStream(Encoding.ASCII.GetBytes(CSharpCode)))
            {
                var result = Cecilifier.Core.Cecilifier.Process(code, GetTrustedAssembliesPath());
                return Redirect($"/CecilifierApplication/?cecilifiedCode={System.Web.HttpUtility.UrlEncode(result.ReadToEnd())}");
                
            }
        }

        public void OnGet()
        {
        }
        
        private IList<string> GetTrustedAssembliesPath()
        {
            return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
        }
    }
}