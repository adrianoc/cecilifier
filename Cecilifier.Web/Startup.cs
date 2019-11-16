using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.WebSockets;
using System.Resources;
using System.Text;
using System.Threading;
using System.Web;
using Cecilifier.Core;
using Cecilifier.Web.Pages;
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
                        CecilifyCode(webSocket);
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

            void CecilifyCode(WebSocket webSocket)
            {
                var buffer = new byte[1024 * 4];
                var result = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
                while (!result.CloseStatus.HasValue)
                {
                    using (var code = new MemoryStream(buffer, 0, result.Count))
                    {
                        CecilifierApplication.Count++;
                        
                        try
                        {
                            var deployKind = code.ReadByte();
                            var cecilifiedCode = Core.Cecilifier.Process(code, GetTrustedAssembliesPath());

                            if (deployKind == 'Z')
                            {
                                var responeData = ZipProject( 
                                    ("Program.cs", cecilifiedCode.GeneratedCode.ReadToEnd()),
                                    ("Cecilified.csproj", @"<Project Sdk=""Microsoft.NET.Sdk"">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>netcoreapp3.0</TargetFramework>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include=""Mono.Cecil"" Version=""0.11.0"" />
    </ItemGroup>
</Project>"),
                                    NameAndContentFromResource("Cecilifier.Web.Runtime")
                                    );

                                var output = new Span<byte>(new byte[8192]);
                                var ret = Base64.EncodeToUtf8(responeData.Span, output,  out var bytesConsumed, out var bytesWritten);
                                if (ret == OperationStatus.Done)
                                {
                                    output = output.Slice(0, bytesWritten);
                                }
                                var r = $"{{ \"status\" : 0, \"counter\": {CecilifierApplication.Count}, \"kind\": \"Z\", \"mainTypeName\":\"{cecilifiedCode.MainTypeName}\", \"cecilifiedCode\" : \"{Encoding.UTF8.GetString(output)}\" }}";
                                var dataToReturn = Encoding.UTF8.GetBytes(r).AsMemory();
                                webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                            }
                            else
                            {
                                var cecilifiedStr = HttpUtility.JavaScriptStringEncode(cecilifiedCode.GeneratedCode.ReadToEnd());
                                var r = $"{{ \"status\" : 0, \"counter\": {CecilifierApplication.Count}, \"kind\": \"C\", \"cecilifiedCode\" : \"{cecilifiedStr}\" }}";
                                var dataToReturn = Encoding.UTF8.GetBytes(r).AsMemory();
                                webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                            }
                        }
                        catch (SyntaxErrorException ex)
                        {
                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 1,  \"syntaxError\": \"{ HttpUtility.JavaScriptStringEncode(ex.Message)}\"  }}").AsMemory();
                            webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 2,  \"error\": \"{ HttpUtility.JavaScriptStringEncode(ex.ToString())}\"  }}").AsMemory();
                            webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                    }

                    result = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
                }
                webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);

                Memory<byte> ZipProject(params (string fileName, string contents)[] sources)
                {
                    /*
                    //TODO: For some reason this code produces an invalid stream. Need to investigate.
                    using var zipStream = new MemoryStream();
                    using var zipFile = new ZipArchive(zipStream, ZipArchiveMode.Create);
                    foreach (var source in sources)
                    {
                        var entry = zipFile.CreateEntry(source.fileName, CompressionLevel.Fastest);
                        using var entryWriter = new StreamWriter(entry.Open());
                        entryWriter.Write(source.contents);
                    }

                    zipStream.Position = 0;
                    Memory<byte> s = zipStream.GetBuffer();
                    Console.WriteLine($"Stream Size = {zipStream.Length}");
                    return s.Slice(0, (int)zipStream.Length);
                    */
                    
                    var tempPath = Path.GetTempPath();
                    var assetsPath = Path.Combine(tempPath, "output");
                    if (Directory.Exists(assetsPath))
                        Directory.Delete(assetsPath, true);
                    
                    Directory.CreateDirectory(assetsPath);
                    
                    foreach (var source in sources)
                    {
                        File.WriteAllText(Path.Combine(assetsPath, $"{source.fileName}"), source.contents);
                    }
                    
                    var outputZipPath = Path.Combine(tempPath, "Cecilified.zip");
                    if (File.Exists(outputZipPath))
                        File.Delete(outputZipPath);

                    ZipFile.CreateFromDirectory(assetsPath, outputZipPath, CompressionLevel.Fastest, false);
                    return File.ReadAllBytes(outputZipPath);
                }
            }
            
            IList<string> GetTrustedAssembliesPath()
            {
                return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
            }
            
        }

        (string fileName, string contents) NameAndContentFromResource(string resourceName)
        {
            var rm = new ResourceManager(resourceName, typeof(Startup).Assembly);
            var contents = rm.GetString("TypeHelpers");
            return ("RuntimeHelper.cs", contents);
        }
    }
}
