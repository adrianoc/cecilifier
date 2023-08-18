using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Cecilifier.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cecilifier.Web
{
    /* 
     * This class implements the following github integration:
     * 1. OAuth authorization/authentication as described in https://docs.github.com/en/developers/apps/building-oauth-apps/authorizing-oauth-apps
     * 2. GitHub issue creation (https://docs.github.com/en/rest/reference/issues#create-an-issue)
     * 3. Retrieving github issues marked as `fixed-in-staging` and also latest releases notes
     * 4. Adding / Removing list of extra assemblies to reference.
     */
    internal static class CecilifierRestHandler
    {
        private const string CecilifierClientId = "5462d562b527fa4e7807";
        internal static ILogger _logger;
        
        // Security token used to access github public api on cecilifier repo.
        // We use this, instead of unauthenticated access, to minimize the impact of rate limits (authenticated access have a much higher limit).
        // Unfortunately AOT these tokens have a validity of no more than 1 year, after that it need to be replaced with a new token.
        private static readonly string _cecilifierGitHubToken; 

        static CecilifierRestHandler()
        {
            _cecilifierGitHubToken = Environment.GetEnvironmentVariable("CECILIFIER_GITHUB_TOKEN");
        }

        internal static async Task FileIssueEndPointAsync(HttpContext context)
        {
            var title = context.Request.Query["title"].ToString();
            var body = context.Request.Query["body"].ToString();

            // Unfortunately labels are ignored if user making the request does not have PUSH permission.
            // We add them, just in case the user does have such permission (most likely this will hold 
            // only for the owner of the repo)
            var issueJson = $"{{ \"body\" : \"{body}\", \"title\" : \"{title}\", \"labels\" : [ \"bug_reporter\"] }}";

            var stateBytes = new byte[64];
            RandomNumberGenerator.Create().GetBytes(stateBytes);
            var stateString = BitConverter.ToString(stateBytes).Replace("-", "");

            await File.WriteAllTextAsync(Path.Combine("/tmp", stateString), issueJson);
            context.Response.Redirect($"https://github.com/login/oauth/authorize?client_id={CecilifierClientId}&scope=public_repo&state={stateString}&redirect_uri={Uri.EscapeDataString($"{context.Request.Headers[HttpRequestHeader.Referer.ToString()]}authorization_callback?id={stateString}")}");
        }

        internal static async Task ReportIssueEndPointAsync(HttpContext context)
        {
            await context.Response.WriteAsync("<html><body>\n");
            try
            {
                if (context.Request.Query.ContainsKey("error"))
                {
                    await context.Response.WriteAsync($"<script>window.opener.postMessage('{{ \"status\": \"error\", \"message\": \"{context.Request.Query["error_description"].ToString()}\" }}','*');</script>");
                    return;
                }

                var client = new HttpClient();
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = CecilifierClientId,
                    ["client_secret"] = Environment.GetEnvironmentVariable("CECILIFIER_BUGREPORTER_SECRET"),
                    ["code"] = context.Request.Query["code"]
                });

                client.DefaultRequestHeaders.Add("Accept", "application/json");
                var accessTokenResponse = await client.PostAsync("https://github.com/login/oauth/access_token", content);

                if (!accessTokenResponse.IsSuccessStatusCode)
                {
                    var accessTokenResponseBody = await accessTokenResponse.Content.ReadAsStringAsync();
                    await context.Response.WriteAsync($"<script>window.opener.postMessage('{{ \"status\": \"error\", \"message\": \"{accessTokenResponseBody}\" }}','*');</script>");
                    return;
                }

                await context.Response.WriteAsync("<p>Processing your authorization..</p>");

                var jsonObj = JsonDocument.Parse(await accessTokenResponse.Content.ReadAsStringAsync());
                var accessToken = jsonObj.RootElement.GetProperty("access_token").GetString();

                using var httpClient = new HttpClient();
                var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/repos/adrianoc/cecilifier/issues");

                msg.Headers.Authorization = new AuthenticationHeaderValue("Token", accessToken);
                msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                msg.Headers.UserAgent.Add(new ProductInfoHeaderValue("cecilifier", "1.0.0"));

                var issueJsonFilePath = Path.Combine("/tmp", context.Request.Query["id"].ToString());
                var issueJson = await File.ReadAllTextAsync(issueJsonFilePath);
                File.Delete(issueJsonFilePath);

                msg.Content = new StringContent(issueJson, Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(msg);
                var responseStr = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.Created)
                {
                    // Parse response body and extract link to issue
                    var issueUrl = ((Newtonsoft.Json.Linq.JObject) Newtonsoft.Json.JsonConvert.DeserializeObject(responseStr))["url"].ToString();

                    // notify cecilifier main page  about the outcome of issue reporting
                    await context.Response.WriteAsync($"<script>window.opener.postMessage('{{ \"status\": \"success\", \"issueUrl\": \"{issueUrl}\" }}','*');</script>");
                }
                else
                {
                    await context.Response.WriteAsync($"<script>window.opener.postMessage('{{ \"status\": \"error\", \"message\": \"{responseStr}\" }}','*');</script>");
                }

            }
            finally
            {
                await context.Response.WriteAsync($"<script>window.close();</script>");
                await context.Response.WriteAsync("</body></html>");
            }
        }

        internal static async Task ReferencedAssembliesEndPointAsync(HttpContext context)
        {
            if (!AssemblyReferenceCacheHandler.HashEnoughStorageSpace(Constants.AssemblyReferenceCacheBasePath))
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Not enough space to store assembly references at server. Please, remove one or more from the list assembly references.");
                return;
            }

            const int bufferMaxLengthInBytes = 1024 * 1024 * 8;
            if (context.Request.Headers.ContentLength > bufferMaxLengthInBytes)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Request to process reference assemblies is to large (request = {context.Request.Headers.ContentLength} bytes, maximum={bufferMaxLengthInBytes} bytes).<br/> Try removing assemblies from the 'Assembly References' list or cecilifying your code after adding each assembly.");
                return;
            }

            string currentAssemblyName = null;
            var assemblyBytes = ArrayPool<byte>.Shared.Rent(bufferMaxLengthInBytes);
            try
            {
                _logger.LogInformation($"{context.Connection.RemoteIpAddress} wants to upload assemblies.");
                var totalBytesRead = 0;
                int readCount;
                do
                {
                    readCount = await context.Request.Body.ReadAsync(assemblyBytes, totalBytesRead, assemblyBytes.Length - totalBytesRead);
                    totalBytesRead += readCount;

                } while (readCount != 0);

                var assembliesToStore = JsonSerializer.Deserialize<AssemblyReferenceList>(assemblyBytes.AsSpan().Slice(0, totalBytesRead));
                foreach (var assemblyReference in assembliesToStore!.AssemblyReferences)
                {
                    currentAssemblyName = assemblyReference.AssemblyName;

                    var assemblyPath = Path.Combine(Constants.AssemblyReferenceCacheBasePath, assemblyReference.AssemblyHash, assemblyReference.AssemblyName);
                    if (Convert.TryFromBase64String(assemblyReference.Base64Contents, assemblyBytes, out var bytesWritten))
                    {
                        await AssemblyReferenceCacheHandler.StoreAssemblyBytesAsync(assemblyPath, assemblyBytes, bytesWritten);
                    }
                    else
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync($"Cecilifier was Unable to store referenced assembly(ies). Assembly name: {currentAssemblyName ?? "N/A"}");

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store reference assemblies.");

                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Cecilifier was Unable to store referenced assembly(ies). Assembly name: {currentAssemblyName ?? "N/A"}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(assemblyBytes);
            }
        }
        
        internal static async Task RetrieveListOfFixedIssuesInStagingServerEndPointAsync(HttpContext context)
        {
            await ExecuteReadOnlyGitHubApiAuthenticated(context, "https://api.github.com/repos/adrianoc/cecilifier/issues?state=open&labels=fixed-in-staging");
        }
        
        internal static async Task RetrieveReleaseNotes(HttpContext context)
        {
            await ExecuteReadOnlyGitHubApiAuthenticated(context, "https://api.github.com/repos/adrianoc/cecilifier/releases");
        }

        private static async Task ExecuteReadOnlyGitHubApiAuthenticated(HttpContext context, string uri)
        {
            if (string.IsNullOrWhiteSpace(_cecilifierGitHubToken))
            {
                _logger.LogWarning($"CECILIFIER_GITHUB_TOKEN not defined. Retrieval of list of bugs fixed in staging/latest release info will not be available.");
                await context.Response.WriteAsync("N/A");
                return;
            }
            
            using var httpClient = new HttpClient();
            var msg = new HttpRequestMessage(HttpMethod.Get, uri);

            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cecilifierGitHubToken);
            msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            msg.Headers.UserAgent.Add(new ProductInfoHeaderValue("cecilifier", "1.0.0"));

            var response = await httpClient.SendAsync(msg);
            if (response.StatusCode != HttpStatusCode.OK)
                context.Response.StatusCode = 500;

            await context.Response.WriteAsync(await response.Content.ReadAsStringAsync());    
        }
    }
}
