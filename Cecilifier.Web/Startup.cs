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
using System.Text.Json.Serialization;
using System.Threading;
using System.Web;
using Cecilifier.Core;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Naming;
using Cecilifier.Web.Pages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cecilifier.Web
{
    public class CecilifiedWebResult
    {
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("cecilifiedCode")] public string CecilifiedCode { get; set; }
        [JsonPropertyName("counter")] public int Counter { get; set; }
        [JsonPropertyName("kind")] public char Kind { get; set; }
        [JsonPropertyName("mappings")] public IList<Mapping> Mappings { get; set; }
        [JsonPropertyName("mainTypeName")] public string MainTypeName { get; set; }
    }
    
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
                var memory = new Memory<byte>(buffer);
                var received = webSocket.ReceiveAsync(memory, CancellationToken.None).Result;
                while (received.MessageType != WebSocketMessageType.Close)
                {
                    CecilifierApplication.Count++;
                    var toBeCecilified = JsonSerializer.Deserialize<CecilifierRequest>(memory.Span[0..received.Count]);
                    var bytes = Encoding.UTF8.GetBytes(toBeCecilified.Code);
                    using (var code = new MemoryStream(bytes, 0, bytes.Length))
                    {
                        try
                        {
                            var deployKind = toBeCecilified.WebOptions.DeployKind;
                            var cecilifiedResult = Core.Cecilifier.Process(code, new CecilifierOptions
                            {
                                References = GetTrustedAssembliesPath(), 
                                Naming = new DefaultNameStrategy(toBeCecilified.Settings.NamingOptions, toBeCecilified.Settings.ElementKindPrefixes.ToDictionary(entry => entry.ElementKind, entry => entry.Prefix))
                            });
                            
                            SendTextMessageToChat("One more happy user (project)",  $"Total so far: {CecilifierApplication.Count}\n\n***********\n\n```{toBeCecilified.Code}```", "4437377");
                            
                            if (deployKind == 'Z')
                            {
                                var responseData = ZipProject(
                                    ("Program.cs", cecilifiedResult.GeneratedCode.ReadToEnd()),
                                    ("Cecilified.csproj", ProjectContents),
                                    NameAndContentFromResource("Cecilifier.Web.Runtime")
                                );

                                var output = new Span<byte>(buffer);
                                var ret = Base64.EncodeToUtf8(responseData.Span, output, out var bytesConsumed, out var bytesWritten);
                                if (ret == OperationStatus.Done)
                                {
                                    output = output[0..bytesWritten];
                                }

                                var dataToReturn = JsonSerializedBytes(Encoding.UTF8.GetString(output), 'Z', cecilifiedResult);
                                webSocket.SendAsync(dataToReturn, received.MessageType, received.EndOfMessage, CancellationToken.None);
                            }
                            else
                            {
                                var dataToReturn = JsonSerializedBytes(cecilifiedResult.GeneratedCode.ReadToEnd(), 'C', cecilifiedResult);
                                webSocket.SendAsync(dataToReturn, received.MessageType, received.EndOfMessage, CancellationToken.None);
                            }
                        }
                        catch (SyntaxErrorException ex)
                        {
                            var source = ((toBeCecilified.Settings.NamingOptions & NamingOptions.IncludeSourceInErrorReports) == NamingOptions.IncludeSourceInErrorReports) ? toBeCecilified.Code : string.Empty;  
                            SendMessageWithCodeToChat("Syntax Error", ex.Message, "15746887", source);

                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 1,  \"syntaxError\": \"{HttpUtility.JavaScriptStringEncode(ex.Message)}\"  }}").AsMemory();
                            webSocket.SendAsync(dataToReturn, received.MessageType, received.EndOfMessage, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToChat(ex, buffer, received.Count);

                            var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 2,  \"error\": \"{HttpUtility.JavaScriptStringEncode(ex.ToString())}\"  }}").AsMemory();
                            webSocket.SendAsync(dataToReturn, received.MessageType, received.EndOfMessage, CancellationToken.None);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    received = webSocket.ReceiveAsync(memory, CancellationToken.None).Result;
                }
                webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

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
            
            IReadOnlyList<string> GetTrustedAssembliesPath()
            {
                return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
            }
        }

        private static byte[] JsonSerializedBytes(string cecilifiedCode, char kind, CecilifierResult cecilifierResult)
        {
            var cecilifiedWebResult = new CecilifiedWebResult
            {
                Status = 0,
                CecilifiedCode = cecilifiedCode,
                Counter = CecilifierApplication.Count,
                Kind = kind,
                MainTypeName = cecilifierResult.MainTypeName,
                Mappings = cecilifierResult.Mappings.OrderBy(x => x.Cecilified.Length).ToArray()
            };

            return JsonSerializer.SerializeToUtf8Bytes(cecilifiedWebResult);
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
        private string CodeInBytesToString(byte[] code, int length)
        {
            var stream = new MemoryStream(code,2, length - 2); // skip byte with info whether user wants zipped project or not & publishing source (discord) or not.
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        
        private void SendMessageWithCodeToChat(string title, string msg, string color, string code)
        {
            SendTextMessageToChat(title,  $"{msg}\n\n***********\n\n```{code}```", color);
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
