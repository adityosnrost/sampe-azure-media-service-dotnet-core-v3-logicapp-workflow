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
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json.Linq;

namespace mncpublishwithaes
{
    public static class CheckUploadBlobStatus
    {
        [FunctionName("CheckUploadBlobStatus")]
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

            // Validate input objects
            if (data.assetName == null)
                return new BadRequestObjectResult("Please pass assetName in the input object");
            string assetName = data.assetName;
            List<string> fileNames = null;
            if (data.fileNames != null)
            {
                fileNames = ((JArray)data.fileNames).ToObject<List<string>>();
            }

            bool copyStatus = true;
            JArray blobCopyStatusList = new JArray();

            Asset asset = null;

            try
            {
                IAzureMediaServicesClient client = await ContentPolicyHelper.CreateMediaServicesClientAsync(config);

                // Set the polling interval for long running operations to 2 seconds.
                // The default value is 30 seconds for the .NET client SDK
                client.LongRunningOperationRetryTimeout = 2;

                asset = await client.Assets.GetAsync(config.ResourceGroup, config.AccountName, assetName);
                if (asset == null)
                    return new BadRequestObjectResult("Asset Not Found.");

                var response = client.Assets.ListContainerSas(config.ResourceGroup, config.AccountName, assetName, permissions: AssetContainerPermission.Read, expiryTime: DateTime.UtcNow.AddHours(4).ToUniversalTime());
                var sasUri = new Uri(response.AssetContainerSasUrls.First());
                CloudBlobContainer destinationBlobContainer = new CloudBlobContainer(sasUri);

                log.LogInformation("Checking CopyStatus of all blobs in the source container...");
                var blobList = BlobStorageHelper.ListBlobs(destinationBlobContainer);
                foreach (var blob in blobList)
                {
                    if (fileNames != null)
                    {
                        bool found = false;
                        foreach (var fileName in fileNames)
                        {
                            if (fileName == blob.Name)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found == false) break;
                    }

                    if (blob.CopyState.Status == CopyStatus.Aborted || blob.CopyState.Status == CopyStatus.Failed)
                    {
                        // Log the copy status description for diagnostics and restart copy
                        await blob.StartCopyAsync(blob.CopyState.Source);
                        copyStatus = false;
                    }
                    else if (blob.CopyState.Status == CopyStatus.Pending)
                    {
                        // We need to continue waiting for this pending copy
                        // However, let us log copy state for diagnostics
                        copyStatus = false;
                    }
                    // else we completed this pending copy

                    string blobName = blob.Name as string;
                    int blobCopyStatus = (int)(blob.CopyState.Status);
                    JObject o = new JObject();
                    o["blobName"] = blobName;
                    o["blobCopyStatus"] = blobCopyStatus;
                    blobCopyStatusList.Add(o);
                }
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

            JObject result = new JObject();
            result["copyStatus"] = copyStatus;
            result["blobCopyStatusList"] = blobCopyStatusList;

            return new OkObjectResult(result);
        }
    }
}
