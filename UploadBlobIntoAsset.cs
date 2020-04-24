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
    public static class UploadBlobIntoAsset
    {
        [FunctionName("UploadBlobIntoAsset")]
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
            if (data.assetId == null)
                return new BadRequestObjectResult("Please pass assetId in the input object");
            if (data.assetName == null)
                return new BadRequestObjectResult("Please pass assetName in the input object");
            if (config.sourceStorageAccountName == null)
                return new BadRequestObjectResult("Please pass sourceStorageAccountName in the input object");
            if (config.sourceStorageAccountKey == null)
                return new BadRequestObjectResult("Please pass sourceStorageAccountKey in the input object.");
            if (data.sourceContainer == null)
                return new BadRequestObjectResult("Please pass sourceContainer in the input object.");

            string _sourceStorageAccountName = config.sourceStorageAccountName;
            string _sourceStorageAccountKey = config.sourceStorageAccountKey;
            string assetId = data.assetId;
            string assetName = data.assetName;
            string sourceContainer = data.sourceContainer;
            string fileName = data.fileName;

            Asset newAsset = null;
            string destinationBlobContainerName = null;

            try
            {
                IAzureMediaServicesClient client = await ContentPolicyHelper.CreateMediaServicesClientAsync(config);

                // Set the polling interval for long running operations to 2 seconds.
                // The default value is 30 seconds for the .NET client SDK
                client.LongRunningOperationRetryTimeout = 2;

                newAsset = await client.Assets.GetAsync(config.ResourceGroup, config.AccountName, assetName);
                if (newAsset == null)
                    return new BadRequestObjectResult("Asset Not Found.");


                CloudBlobContainer sourceBlobContainer = BlobStorageHelper.GetCloudBlobContainer(_sourceStorageAccountName, _sourceStorageAccountKey, sourceContainer);
                
                var response = client.Assets.ListContainerSas(config.ResourceGroup, config.AccountName, assetName, permissions: AssetContainerPermission.ReadWrite, expiryTime: DateTime.UtcNow.AddHours(4).ToUniversalTime());
                
                var sasUri = new Uri(response.AssetContainerSasUrls.First());
                CloudBlobContainer destinationBlobContainer = new CloudBlobContainer(sasUri);
                destinationBlobContainerName = destinationBlobContainer.Name;

                CloudBlob sourceBlob = sourceBlobContainer.GetBlockBlobReference(fileName);
                CloudBlob destinationBlob = destinationBlobContainer.GetBlockBlobReference(fileName);
                CopyBlobAsync(sourceBlob as CloudBlob, destinationBlob);

                return new OkObjectResult(
                    new
                    {
                        destinationContainer = destinationBlobContainerName,
                        nameAsset = assetName
                    }
                    );
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

        static public async void CopyBlobAsync(CloudBlob sourceBlob, CloudBlob destinationBlob)
        {
            var signature = sourceBlob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24)
            });
            await destinationBlob.StartCopyAsync(new Uri(sourceBlob.Uri.AbsoluteUri + signature));
        }
    }
}
