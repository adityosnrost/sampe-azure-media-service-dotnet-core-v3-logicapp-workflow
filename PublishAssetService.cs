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
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace mncpublishwithaes
{
    public static class PublishAssetService
    {
        [FunctionName("PublishAssetService")]
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

                // Set the polling interval for long running operations to 2 seconds.
                // The default value is 30 seconds for the .NET client SDK
                client.LongRunningOperationRetryTimeout = 2;

                Asset currentAsset = client.Assets.Get(config.ResourceGroup, config.AccountName, name);

                // Generate Content Key
                //if (!StaticData.ContentKeyIds.TryGetValue(name, out Guid contentKeyId))
                //{
                //    contentKeyId = new Guid();
                //    StaticData.ContentKeyIds.Add(name, contentKeyId);
                //}

                var randomBytes = new byte[40];
                //new RNGCryptoServiceProvider().GetBytes(randomBytes);
                //var contentKeyValue = Convert.ToBase64String(randomBytes);

                //var contentKeys = new List<StreamingLocatorContentKey> {
                //    new StreamingLocatorContentKey(
                //        id: contentKeyId,
                //        type: StreamingLocatorContentKeyType.EnvelopeEncryption,
                //        labelReferenceInStreamingPolicy: name,
                //        value: contentKeyValue,
                //        policyName: StaticData.ContentKeyPolicyName)
                //};

                //Create the content key policy that configures how the content key is delivered to end clients
                // via the Key Delivery component of Azure Media Services.
                ContentKeyPolicy policy = ContentPolicyHelper.GetOrCreateContentKeyPolicy(client, config.ResourceGroup, config.AccountName, StaticData.ContentKeyPolicyName, randomBytes);

                // Generate Locator for new Asset or adding new locator to current Asset
                string uniqueness = Guid.NewGuid().ToString().Substring(0, 13);

                string streamingLocatorName = "locator-" + uniqueness;
                StreamingLocator locator = new StreamingLocator(
                        assetName: currentAsset.Name,
                        streamingPolicyName: PredefinedStreamingPolicy.ClearKey,
                        defaultContentKeyPolicyName: StaticData.ContentKeyPolicyName);
                        //contentKeys: contentKeys);
                client.StreamingLocators.Create(config.ResourceGroup, config.AccountName, streamingLocatorName, locator);

                return new OkObjectResult($"Asset {name} is successfully published");
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
