using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Cecilifier.Core.Misc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cecilifier.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseWebSockets();
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseMvc();
            
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await CecilifierCode(context, webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }

            });
            
            async Task CecilifierCode(HttpContext context, WebSocket webSocket)
            {
                var buffer = new byte[1024 * 4];
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                while (!result.CloseStatus.HasValue)
                {
                    using (var code = new MemoryStream(buffer, 0, result.Count))
                    {
                        try
                        {
                            var cecilifiedCode = Core.Cecilifier.Process(code, GetTrustedAssembliesPath());
                            
                            var cecilifiedStr = HttpUtility.JavaScriptStringEncode(cecilifiedCode.ReadToEnd());
                            
                            var r = $"{{ \"status\" : 0, \"cecilifiedCode\" : \"{cecilifiedStr}\" }}";
                            var dataToReturn = Encoding.UTF8.GetBytes(r).AsMemory();
                            await webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                        catch (SyntaxErrorException ex)
                        {
                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 1,  \"syntaxError\": \"{ HttpUtility.JavaScriptStringEncode(ex.Message)}\"  }}").AsMemory();
                            await webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 2,  \"error\": \"{ HttpUtility.JavaScriptStringEncode(ex.ToString())}\"  }}").AsMemory();
                            await webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                    }

                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
                await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
            }
            
            IList<string> GetTrustedAssembliesPath()
            {
                return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
            }
            
        }
    }
}
