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
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Rest;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json.Linq;

namespace mncpublishwithaes
{
    public static class EncodeAsset
    {
        [FunctionName("EncodeAsset")]
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

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            if (data.assetId == null)
                return new BadRequestObjectResult("Please pass assetId in the input object");

            if (data.assetName == null)
                return new BadRequestObjectResult("Please pass assetName in the input object");

            string assetId = data.assetId;
            string assetName = data.assetName;

            try
            {
                IAzureMediaServicesClient client = await ContentPolicyHelper.CreateMediaServicesClientAsync(config);

                // Set the polling interval for long running operations to 2 seconds.
                // The default value is 30 seconds for the .NET client SDK
                client.LongRunningOperationRetryTimeout = 2;

                string uniqueness = Guid.NewGuid().ToString("N");

                string outputAssetName = $"encoded-{assetName}-{uniqueness}";
                string jobName = $"job-{assetName}-{uniqueness}";

                // Ensure that you have the desired encoding Transform. This is really a one time setup operation.
                Transform transform = await GetOrCreateTransformAsync(client, config.ResourceGroup, config.AccountName, config.AdaptiveStreamingTransformName);

                Asset checkOutputAsset = await client.Assets.GetAsync(config.ResourceGroup, config.AccountName, outputAssetName);

                if (checkOutputAsset != null)
                {
                    // Name collision! In order to get the sample to work, let's just go ahead and create a unique asset name
                    // Note that the returned Asset can have a different name than the one specified as an input parameter.
                    // You may want to update this part to throw an Exception instead, and handle name collisions differently.
                    string uniqueness2 = $"-{Guid.NewGuid().ToString("N")}";
                    outputAssetName += uniqueness2;
                }
                Asset outputAsset = await client.Assets.CreateOrUpdateAsync(config.ResourceGroup, config.AccountName, outputAssetName, new Asset());

                JobInput jobInput = new JobInputAsset(assetName: assetName);
                JobOutput[] jobOutputs =
                {
                    new JobOutputAsset(outputAssetName),
                };

                // In this example, we are assuming that the job name is unique.
                //
                // If you already have a job with the desired name, use the Jobs.Get method
                // to get the existing job. In Media Services v3, the Get method on entities returns null 
                // if the entity doesn't exist (a case-insensitive check on the name).
                Job job = await client.Jobs.CreateAsync
                    (
                    config.ResourceGroup,
                    config.AccountName,
                    config.AdaptiveStreamingTransformName,
                    jobName,
                    new Job
                    {
                        Input = jobInput,
                        Outputs = jobOutputs,
                    });

                JObject result = new JObject();
                result["jobName"] = jobName;
                result["transformName"] = config.AdaptiveStreamingTransformName;
                return new OkObjectResult(result);
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

        private static async Task<Transform> GetOrCreateTransformAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName)
        {
            // Does a Transform already exist with the desired name? Assume that an existing Transform with the desired name
            // also uses the same recipe or Preset for processing content.
            Transform transform = await client.Transforms.GetAsync(resourceGroupName, accountName, transformName);

            if (transform == null)
            {
                // You need to specify what you want it to produce as an output
                TransformOutput[] output = new TransformOutput[]
                {
                    new TransformOutput
                    {
                        // The preset for the Transform is set to one of Media Services built-in sample presets.
                        // You can  customize the encoding settings by changing this to use "StandardEncoderPreset" class.
                        Preset = new BuiltInStandardEncoderPreset()
                        {
                            // This sample uses the built-in encoding preset for Adaptive Bitrate Streaming.
                            PresetName = EncoderNamedPreset.AdaptiveStreaming
                        }
                    }
                };

                // Create the Transform with the output defined above
                transform = await client.Transforms.CreateOrUpdateAsync(resourceGroupName, accountName, transformName, output);
            }

            return transform;
        }
    }
}
