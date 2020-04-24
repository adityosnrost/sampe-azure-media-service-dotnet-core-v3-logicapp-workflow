using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Extensions.Configuration;

namespace mncpublishwithaes
{
    public static class TokenGeneratorService
    {
        [FunctionName("TokenGeneratorService")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var currentDirectory = "/home/site/wwwroot";
            bool isLocal = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
            if (isLocal)
            {
                currentDirectory = Directory.GetCurrentDirectory();
            }


            ConfigWrapper config = new ConfigWrapper(new ConfigurationBuilder()
                .SetBasePath(currentDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build());

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            try
            {
                IAzureMediaServicesClient client = await ContentPolicyHelper.CreateMediaServicesClientAsync(config);
                Console.WriteLine("connected");

                // Set the polling interval for long running operations to 2 seconds.
                // The default value is 30 seconds for the .NET client SDK
                client.LongRunningOperationRetryTimeout = 2;

                Asset currentAsset = client.Assets.Get(config.ResourceGroup, config.AccountName, name);

                // Get the Content Key Id
                //if (!StaticData.ContentKeyIds.TryGetValue(name, out Guid contentKeyId))
                //    return new BadRequestObjectResult("Can't find content key ID for this run, please republish the asset (Note: This error is only due to this demo code.)");

                //Create the content key policy that configures how the content key is delivered to end clients
                // via the Key Delivery component of Azure Media Services.
                ContentKeyPolicy policy = ContentPolicyHelper.GetOrCreateContentKeyPolicy(client, config.ResourceGroup, config.AccountName, StaticData.ContentKeyPolicyName);

                //Listing streaming locators for this asset and get the first one as default locator
                var locators = client.Assets.ListStreamingLocators(config.ResourceGroup, config.AccountName, currentAsset.Name);
                var streamingLocatorName = locators.StreamingLocators.First().Name;

                /************************************/
                /** Uncomment if using AES with JWT */
                var response = client.StreamingLocators.ListContentKeys(config.ResourceGroup, config.AccountName, streamingLocatorName);
                var contentKeyId = response.ContentKeys.First().Id.ToString();
                var policyProperties = client.ContentKeyPolicies.GetPolicyPropertiesWithSecrets(config.ResourceGroup, config.AccountName, StaticData.ContentKeyPolicyName);

                // Generate AES Token for streaming use
                var token = ContentPolicyHelper.GetToken(StaticData.Issuer, StaticData.Audience, contentKeyId, policyProperties);

                /************************************/

                var streamingEndpoint = client.StreamingEndpoints.Get(config.ResourceGroup, config.AccountName, "default");
                // Get the URls to stream the output
                var paths = client.StreamingLocators.ListPaths(config.ResourceGroup, config.AccountName, streamingLocatorName);

                List<string> urlList = new List<string>();

                string streamingURL = "";

                for (int i = 0; i < paths.StreamingPaths.Count; i++)
                {
                    UriBuilder uriBuilder = new UriBuilder();
                    uriBuilder.Scheme = "https";
                    uriBuilder.Host = streamingEndpoint.HostName;

                    if (paths.StreamingPaths[i].Paths.Count > 0)
                    {
                        if (paths.StreamingPaths[i].StreamingProtocol == StreamingPolicyStreamingProtocol.Dash)
                        {
                            uriBuilder.Path = paths.StreamingPaths[i].Paths[0];
                            var dashPath = uriBuilder.ToString();

                            /** Uncomment if using AES with JWT */
                            streamingURL = $"https://ampdemo.azureedge.net/?url={dashPath}&aes=true&aestoken=Bearer%3D{token}";

                            /** Uncomment if using AES with Open Restriction */
                            //streamingURL = $"https://ampdemo.azureedge.net/?url={dashPath}&aes=true";
                        }
                    }
                }

                Dictionary<string, string> responseBody = new Dictionary<string, string>();
                responseBody.Add("fullURL", streamingURL);
                //responseBody.Add("token", token);

                return streamingURL != null
                                ? (ActionResult)new OkObjectResult(responseBody)
                                : new BadRequestObjectResult("No url generated");
            }
            catch (Exception exception)
            {
                if (exception.Source.Contains("ActiveDirectory"))
                {
                    return new BadRequestObjectResult("TIP: Make sure that you have filled out the appsettings.json file before running this sample.");
                }

                ApiErrorException apiException = exception.GetBaseException() as ApiErrorException;
                if (apiException != null)
                {
                    return new BadRequestObjectResult($"ERROR: API call failed with error code '{apiException.Body.Error.Code}' and message '{apiException.Body.Error.Message}'.");
                }

                return new BadRequestObjectResult($"{exception.Message}");
            }
        }
    }
}
