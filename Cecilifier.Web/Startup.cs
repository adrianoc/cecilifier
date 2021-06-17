using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Web;
using Cecilifier.Core;
using Cecilifier.Web.Pages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cecilifier.Web
{
    public class Startup
    {
        private const string ProjectContents = @"<Project Sdk=""Microsoft.NET.Sdk"">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>netcoreapp3.0</TargetFramework>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include=""Mono.Cecil"" Version=""0.11.0"" />
    </ItemGroup>
</Project>";
        
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
            
            services.AddRazorPages();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
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
            app.UseCookiePolicy();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapRazorPages(); });
            
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
                var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                var result = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
                while (!result.CloseStatus.HasValue)
                {
                    using (var code = new MemoryStream(buffer, 0, result.Count))
                    {
                        CecilifierApplication.Count++;
                        try
                        {
                            var deployKind = code.ReadByte();
                            var publishSourcePolicy = code.ReadByte();
                            
                            var cecilifiedCode = Core.Cecilifier.Process(code, GetTrustedAssembliesPath());

                            if (deployKind == 'Z')
                            {
                                if (publishSourcePolicy == 'A')
                                    SendMessageWithCodeToChat("One more happy user (project)", $"Total so far: {CecilifierApplication.Count}", "4437377", buffer, result.Count);
                                
                                var responseData = ZipProject(
                                    ("Program.cs", cecilifiedCode.GeneratedCode.ReadToEnd()),
                                    ("Cecilified.csproj", ProjectContents),
                                    NameAndContentFromResource("Cecilifier.Web.Runtime")
                                );

                                var output = new Span<byte>(buffer);
                                var ret = Base64.EncodeToUtf8(responseData.Span, output, out var bytesConsumed, out var bytesWritten);
                                if (ret == OperationStatus.Done)
                                {
                                    output = output.Slice(0, bytesWritten);
                                }

                                var r =
                                    $"{{ \"status\" : 0, \"counter\": {CecilifierApplication.Count}, \"kind\": \"Z\", \"mainTypeName\":\"{cecilifiedCode.MainTypeName}\", \"cecilifiedCode\" : \"{Encoding.UTF8.GetString(output)}\" }}";
                                var dataToReturn = Encoding.UTF8.GetBytes(r).AsMemory();
                                webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                            }
                            else
                            {
                                if (publishSourcePolicy == 'A')
                                    SendMessageWithCodeToChat("One more happy user", $"Total so far: {CecilifierApplication.Count}", "4437377", buffer, result.Count);
                                
                                var cecilifiedStr = HttpUtility.JavaScriptStringEncode(cecilifiedCode.GeneratedCode.ReadToEnd());
                                var r = $"{{ \"status\" : 0, \"counter\": {CecilifierApplication.Count}, \"kind\": \"C\", \"cecilifiedCode\" : \"{cecilifiedStr}\" }}";
                                var dataToReturn = Encoding.UTF8.GetBytes(r).AsMemory();
                                webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                            }
                        }
                        catch (SyntaxErrorException ex)
                        {
                            SendSyntaxErrorToChat(ex, buffer, result.Count);

                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 1,  \"syntaxError\": \"{HttpUtility.JavaScriptStringEncode(ex.Message)}\"  }}").AsMemory();
                            webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToChat(ex, buffer, result.Count);

                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 2,  \"error\": \"{HttpUtility.JavaScriptStringEncode(ex.ToString())}\"  }}").AsMemory();
                            webSocket.SendAsync(dataToReturn, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
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

        private void SendExceptionToChat(Exception exception, byte []code, int length)
        {
            var stasktrace = JsonEncodedText.Encode(exception.StackTrace);
            
            var toSend = $@"{{
            ""content"":""Exception processing request : {JsonEncodedText.Encode(exception.Message)}"",
            ""embeds"": [
            {{
                ""description"": ""{stasktrace}"",
                ""fields"": [
                    {{        
                        ""name"": ""C# Code"",
                        ""value"": ""{JsonEncodedText.Encode(CodeInBytesToString(code, length))}""
                    }}
                ],
                ""color"": ""15746887""
            }}
            ]
        }}";
            
            SendJsonMessageToChat(toSend);            
        }
 
        private void SendSyntaxErrorToChat(SyntaxErrorException syntaxErrorException, byte[] code, int length)
        {
            SendMessageWithCodeToChat("Syntax Error",  syntaxErrorException.Message, "15746887", code, length);
        }
        
        private string CodeInBytesToString(byte[] code, int length)
        {
            var stream = new MemoryStream(code,2, length - 2); // skip byte with info whether user wants zipped project or not & publishing source (discord) or not.
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        
        private void SendMessageWithCodeToChat(string title, string msg, string color, byte[] code, int length)
        {
            var stream = new MemoryStream(code,2, length - 2); // skip byte with info whether user wants zipped project or not & publishing source (discord) or not.
            using var reader = new StreamReader(stream);
            SendTextMessageToChat(title,  $"{msg}\n\n***********\n\n```{reader.ReadToEnd()}```", color);
        }

        private void SendJsonMessageToChat(string jsonMessage)
        {
            var discordChannelUrl = Configuration["DiscordChannelUrl"];
            if (string.IsNullOrWhiteSpace(discordChannelUrl))
            {
                Console.WriteLine("DiscordChannelUrl not specified in configuration file.");
                return;
            }            
            
            var discordPostRequest = WebRequest.Create(discordChannelUrl);
            discordPostRequest.ContentType = "application/json";
            discordPostRequest.Method = WebRequestMethods.Http.Post;

            var buffer = Encoding.ASCII.GetBytes(jsonMessage);
            
            discordPostRequest.ContentLength = buffer.Length;
            var stream = discordPostRequest.GetRequestStream();
            stream.Write(buffer, 0, buffer.Length);
            stream.Close();

            try
            {
                var response = (HttpWebResponse) discordPostRequest.GetResponse();
                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    Console.WriteLine($"Discord returned status: {response.StatusCode}");
                }
                
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unable to send message to discord channel. Exception caught:\n\n{e}");
            }
        }
        
        private void SendTextMessageToChat(string title, string msg, string color)
        {
            var toSend = $@"{{
            ""embeds"": [
            {{
                ""title"": ""{title}"",
                ""description"": ""{JsonEncodedText.Encode(msg)}"",
                ""color"": {color}
            }}
            ]
        }}";

            SendJsonMessageToChat(toSend);
        }
        

        (string fileName, string contents) NameAndContentFromResource(string resourceName)
        {
            var rm = new ResourceManager(resourceName, typeof(Startup).Assembly);
            var contents = rm.GetString("TypeHelpers");
            return ("RuntimeHelper.cs", contents);
        }
    }
}
