using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
        [JsonPropertyName("clientsCounter")] public uint Clients { get; set; }
        [JsonPropertyName("maximumUnique")] public uint MaximumUnique { get; set; }
        [JsonPropertyName("kind")] public char Kind { get; set; }
        [JsonPropertyName("mappings")] public IList<Mapping> Mappings { get; set; }
        [JsonPropertyName("mainTypeName")] public string MainTypeName { get; set; }
    }

    public class Startup
    {
        private const string ProjectContents = """
                                               <Project Sdk="Microsoft.NET.Sdk">
                                                   <PropertyGroup>
                                                       <OutputType>Exe</OutputType>
                                                       <TargetFramework>net8.0</TargetFramework>
                                                   </PropertyGroup>
                                                   <ItemGroup>
                                                       <PackageReference Include="Mono.Cecil" Version="0.11.5" />
                                                       <PackageReference Include="Cecilifier.TypeMapGenerator" Version="1.0.0" />
                                                   </ItemGroup>
                                               </Project>
                                               """;

        private const string NugetConfigForCecilifiedProject = """
                                                               <configuration>
                                                                   <packageSources>
                                                                       <add key="CecilifierCodeGenerators" value="./NugetLocalRepo/" />
                                                                   </packageSources>
                                                               </configuration>
                                                               """;

        private static HttpClient discordConnection = new();
        private static IDictionary<long, uint> seemClientIPHashCodes = new Dictionary<long, uint>();

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

            var webSocketOptions = new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(45),
            };
            app.UseWebSockets(webSocketOptions);

            app.UseCookiePolicy();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapGet("/issues_closed_in_staging", CecilifierRestHandler.RetrieveListOfFixedIssuesInStagingServerEndPointAsync);
                endpoints.MapGet("/get_release_notes", CecilifierRestHandler.RetrieveReleaseNotes);
                endpoints.MapPost("/referenced_assemblies", CecilifierRestHandler.ReferencedAssembliesEndPointAsync);
                endpoints.MapGet("/fileissue", CecilifierRestHandler.FileIssueEndPointAsync);
                endpoints.MapGet("/authorization_callback", CecilifierRestHandler.ReportIssueEndPointAsync);
            });

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        // if cecilifier is deployed in an environment with a reverse proxy (for instance nginx) we will configure the proxy to forward the client IP and use that IP
                        // instead `context.Connection.RemoteIpAddress` which will always be the proxy's IP.
                        var forwardedClientIP = context.Request.Headers["X-Forwarded-For"];
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        
                        await SendStatisticsAsync(webSocket);
                        await CecilifyCodeAsync(webSocket, string.IsNullOrWhiteSpace(forwardedClientIP) ? context.Connection.RemoteIpAddress.GetHashCode() : forwardedClientIP.GetHashCode());
                    }
                    else
                    {
                        context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                    }
                }
                else
                {
                    await next();
                }
            });
            
            async Task SendStatisticsAsync(WebSocket webSocket)
            {
                var cecilifiedWebResult = new CecilifiedWebResult
                {
                    Status = 4,
                    CecilifiedCode = String.Empty,
                    Counter = CecilifierApplication.Count,
                    MaximumUnique = CecilifierApplication.MaximumUnique,
                    Clients = CecilifierApplication.UniqueClients,
                    Kind = 'F',
                    MainTypeName = String.Empty,
                    Mappings = Array.Empty<Mapping>(),
                };

                var dataToReturn = JsonSerializer.SerializeToUtf8Bytes(cecilifiedWebResult);
                await webSocket.SendAsync(dataToReturn, WebSocketMessageType.Text, true, CancellationToken.None);
            }

            async Task CecilifyCodeAsync(WebSocket webSocket, int remoteIpAddressHashCode)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(1024 * 128);
                try
                {
                    await ProcessWebSocketAsync(webSocket, remoteIpAddressHashCode, buffer);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }

        private async Task ProcessWebSocketAsync(WebSocket webSocket, int remoteIpAddressHashCode, byte[] buffer)
        {
            var memory = new Memory<byte>(buffer);
            var received = await webSocket.ReceiveAsync(memory, CancellationToken.None);
            while (received.MessageType != WebSocketMessageType.Close)
            {
                UpdateStatistics(remoteIpAddressHashCode);
                
                var codeSnippet = string.Empty;
                bool includeSourceInErrorReports = false;

                try
                {
                    var toBeCecilified = JsonSerializer.Deserialize<CecilifierRequest>(memory.Span[0..received.Count]);
                    var userAssemblyReferences = AssemblyReferenceCacheHandler.RetrieveAssemblyReferences(Constants.AssemblyReferenceCacheBasePath, toBeCecilified.AssemblyReferences);

                    if (userAssemblyReferences.NotFound.Count > 0)
                    {
                        var dataWritten = Encoding.UTF8
                            .GetBytes(
                                $"{{ \"status\" : 3,  \"originalFormat\": \"{toBeCecilified.WebOptions.DeployKind}\", \"missingAssemblies\": [ {string.Join(',', userAssemblyReferences.NotFound.Select(ma => $"\"{ma}\""))} ] }}",
                                memory.Span);

                        await webSocket.SendAsync(memory.Slice(0, dataWritten), WebSocketMessageType.Text, true, CancellationToken.None);
                        received = await webSocket.ReceiveAsync(memory, CancellationToken.None);

                        continue;
                    }

                    codeSnippet = toBeCecilified.Code;
                    includeSourceInErrorReports = (toBeCecilified.Settings.NamingOptions & NamingOptions.IncludeSourceInErrorReports) == NamingOptions.IncludeSourceInErrorReports;

                    var bytes = Encoding.UTF8.GetBytes(toBeCecilified.Code);
                    await using var code = new MemoryStream(bytes, 0, bytes.Length);

                    var deployKind = toBeCecilified.WebOptions.DeployKind;
                    var cecilifiedResult = Core.Cecilifier.Process(code,
                        new CecilifierOptions
                        {
                            References = GetTrustedAssembliesPath().Concat(userAssemblyReferences.Success).ToList(),
                            Naming = new DefaultNameStrategy(toBeCecilified.Settings.NamingOptions, toBeCecilified.Settings.ElementKindPrefixes.ToDictionary(entry => entry.ElementKind, entry => entry.Prefix))
                        });

                    await SendTextMessageToChatAsync($"One more happy user {(deployKind == 'Z' ? "(project)" : "")}", $"Total so far: {CecilifierApplication.Count}\n\n***********\n\n```{toBeCecilified.Code}```", "4437377");

                    if (deployKind == 'Z')
                    {
                        var responseData = ZipProject(
                            ("Program.cs", await cecilifiedResult.GeneratedCode.ReadToEndAsync()),
                            ("Cecilified.csproj", ProjectContents),
                            ("nuget.config", NugetConfigForCecilifiedProject),
                            ("NugetLocalRepo/Cecilifier.TypeMapGenerator.1.0.0.nupkg", "file-relative-path://Cecilifier.TypeMapGenerator.1.0.0.nupkg"),
                            NameAndContentFromResource("Cecilifier.Web.Runtime")
                        );

                        var output = new Memory<byte>(buffer);
                        var ret = Base64.EncodeToUtf8(responseData.Span, output.Span, out var bytesConsumed, out var bytesWritten);
                        if (ret == OperationStatus.Done)
                        {
                            output = output[0..bytesWritten];
                        }

                        var dataToReturn = JsonSerializedBytes(Encoding.UTF8.GetString(output.Span), 'Z', cecilifiedResult);
                        await webSocket.SendAsync(dataToReturn, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    else
                    {
                        var dataToReturn = JsonSerializedBytes(cecilifiedResult.GeneratedCode.ReadToEnd(), 'C', cecilifiedResult);
                        await webSocket.SendAsync(dataToReturn, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (SyntaxErrorException ex)
                {
                    var source = includeSourceInErrorReports ? codeSnippet : string.Empty;
                    await SendMessageWithCodeToChatAsync("Syntax Error", string.Join('\n', ex.Errors.Select(err => err.Message)), "15746887", source);

                    var jsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Default);
                    jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    var serializedErrors = JsonSerializer.Serialize(ex.Errors, jsonOptions);
                    var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 1, \"error\": \"Code contains syntax errors\", \"errors\": {serializedErrors} }}").AsMemory();
                    await webSocket.SendAsync(dataToReturn, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    await SendExceptionToChatAsync(ex, buffer, received.Count);

                    var dataToReturn = Encoding.UTF8.GetBytes($"{{ \"status\" : 2,  \"error\": \"{HttpUtility.JavaScriptStringEncode(ex.ToString())}\"  }}").AsMemory();
                    await webSocket.SendAsync(dataToReturn, WebSocketMessageType.Text, true, CancellationToken.None);
                }

                received = await webSocket.ReceiveAsync(memory, CancellationToken.None);
            }

            static void UpdateStatistics(int remoteIpAddressHashCode)
            {
                Interlocked.Increment(ref CecilifierApplication.Count);
                if (!seemClientIPHashCodes.TryGetValue(remoteIpAddressHashCode, out var visits))
                {
                    Interlocked.Increment(ref CecilifierApplication.UniqueClients);
                }
                seemClientIPHashCodes[remoteIpAddressHashCode] = visits + 1;
                Interlocked.Exchange(ref CecilifierApplication.MaximumUnique, seemClientIPHashCodes.Values.Max());
            }

            IEnumerable<string> GetTrustedAssembliesPath()
            {
                return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
            }

            Memory<byte> ZipProject(params (string fileName, string contents)[] files)
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

                foreach (var file in files)
                {
                    var filePath = Path.Combine(assetsPath, $"{file.fileName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    if (file.contents.StartsWith("file-relative-path://"))
                    {
                        var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                        File.Copy(Path.Combine(basePath, file.contents.Substring("file-relative-path://".Length)), filePath, true);
                    }
                    else
                    {
                        File.WriteAllText(filePath, file.contents);    
                    }
                }

                var outputZipPath = Path.Combine(tempPath, "Cecilified.zip");
                if (File.Exists(outputZipPath))
                    File.Delete(outputZipPath);

                ZipFile.CreateFromDirectory(assetsPath, outputZipPath, CompressionLevel.Fastest, false);
                return File.ReadAllBytes(outputZipPath);
            }
        }
        
        private static byte[] JsonSerializedBytes(string cecilifiedCode, char kind, CecilifierResult cecilifierResult)
        {
            var cecilifiedWebResult = new CecilifiedWebResult
            {
                Status = 0,
                CecilifiedCode = cecilifiedCode,
                Counter = CecilifierApplication.Count,
                MaximumUnique = CecilifierApplication.MaximumUnique,
                Clients = CecilifierApplication.UniqueClients,
                Kind = kind,
                MainTypeName = cecilifierResult.MainTypeName,
                Mappings = cecilifierResult.Mappings.OrderBy(x => x.Cecilified.Length).ToArray(),
            };

            return JsonSerializer.SerializeToUtf8Bytes(cecilifiedWebResult);
        }

        private Task SendExceptionToChatAsync(Exception exception, byte[] code, int length)
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

            return SendJsonMessageToChatAsync(toSend);
        }
        
        private string CodeInBytesToString(byte[] code, int length)
        {
            var stream = new MemoryStream(code, 2, length - 2); // skip byte with info whether user wants zipped project or not & publishing source (discord) or not.
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private Task SendMessageWithCodeToChatAsync(string title, string msg, string color, string code)
        {
            return SendTextMessageToChatAsync(title, $"{msg}\n\n***********\n\n```{code}```", color);
        }

        private async Task SendJsonMessageToChatAsync(string jsonMessage)
        {
            var discordChannelUrl = Configuration["DiscordChannelUrl"];
            if (string.IsNullOrWhiteSpace(discordChannelUrl))
            {
                Console.WriteLine("DiscordChannelUrl not specified in configuration file.");
                return;
            }

            using var discordPostRequest = new HttpRequestMessage(HttpMethod.Post, discordChannelUrl);
            discordPostRequest.Content = new StringContent(jsonMessage, Encoding.UTF8, "application/json");
            try
            {
                using var response =  await discordConnection.SendAsync(discordPostRequest);
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

        private Task SendTextMessageToChatAsync(string title, string msg, string color)
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

            return SendJsonMessageToChatAsync(toSend);
        }

        (string fileName, string contents) NameAndContentFromResource(string resourceName)
        {
            var rm = new ResourceManager(resourceName, typeof(Startup).Assembly);
            var contents = rm.GetString("TypeHelpers");
            return ("RuntimeHelper.cs", contents);
        }
    }
}
