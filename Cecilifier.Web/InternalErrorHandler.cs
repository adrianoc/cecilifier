using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Cecilifier.Web
{
    /* 
     * This class implements the following github integration:
     * 1. OAuth authorization/authentication as described in https://docs.github.com/en/developers/apps/building-oauth-apps/authorizing-oauth-apps
     * 2. GitHub issue creation (https://docs.github.com/en/rest/reference/issues#create-an-issue)
     */
    internal class InternalErrorHandler
    {
        private const string CecilifierClientId = "5462d562b527fa4e7807";

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
                    var issueURL = ((Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(responseStr))["url"].ToString();

                    // notify cecilifier main page  about the outcome of issue reporting
                    await context.Response.WriteAsync($"<script>window.opener.postMessage('{{ \"status\": \"success\", \"issueUrl\": \"{issueURL}\" }}','*');</script>");
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
    }
}
