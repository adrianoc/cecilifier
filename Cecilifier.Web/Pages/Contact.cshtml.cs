using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cecilifier.Web.Pages
{
    public class ContactModel : PageModel
    {
        public string Message { get; set; } = "";
    }
}
