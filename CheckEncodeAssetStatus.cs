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
    public static class CheckEncodeAssetStatus
    {
        [FunctionName("CheckEncodeAssetStatus")]
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
            if (data.jobName == null)
                return new BadRequestObjectResult("Please pass jobName in the input object");
            if (data.transformName == null)
                return new BadRequestObjectResult("Please pass transformName in the input object");
            string jobName = data.jobName;
            string transformName = data.transformName;

            Job job = null;

            try
            {
                IAzureMediaServicesClient client = await ContentPolicyHelper.CreateMediaServicesClientAsync(config);

                // Set the polling interval for long running operations to 2 seconds.
                // The default value is 30 seconds for the .NET client SDK
                client.LongRunningOperationRetryTimeout = 2;

                job = client.Jobs.Get(config.ResourceGroup, config.AccountName, transformName, jobName);
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
            result["jobStatus"] = job.State.ToString();
            JArray jobOutputStateList = new JArray();
            foreach (JobOutputAsset o in job.Outputs)
            {
                JObject jobOutputState = new JObject();
                jobOutputState["AssetName"] = o.AssetName;
                jobOutputState["State"] = o.State.ToString();
                jobOutputState["Progress"] = o.Progress;
                if (o.Error != null)
                {
                    jobOutputState["ErrorCode"] = o.Error.Code.ToString();
                    jobOutputState["ErrorMessage"] = o.Error.Message.ToString();
                }
                jobOutputStateList.Add(jobOutputState);
            }
            result["jobOutputStateList"] = jobOutputStateList;

            return new OkObjectResult(result);
        }
    }
}
