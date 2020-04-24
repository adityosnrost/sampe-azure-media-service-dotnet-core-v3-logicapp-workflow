# sampe-azure-media-service-dotnet-core-v3-logicapp-workflow

This repo is content Azure Media Service automation using Azure Functions and Logic Apps. Automation start from ingest from Azure Storage, create Asset, Encode, create ContentPolicy, Encrypt, and Publish streaming endpoint.


## Prerequisites for a sample Logic Apps deployments

### 1. Create an Azure Media Services account

Create a Media Services account in your subscription if don't have it already ([follow this article](https://docs.microsoft.com/en-us/azure/media-services/previous/media-services-portal-create-account)).

### 2. Create a Service Principal

Create a Service Principal and save the password. It will be needed in step #4. To do so, go to the API tab in the account ([follow this article](https://docs.microsoft.com/en-us/azure/media-services/media-services-portal-get-started-with-aad#service-principal-authentication)).

### 3. Make sure the AMS streaming endpoint is started

To enable streaming, go to the Azure portal, select the Azure Media Services account which has been created, and start the default streaming endpoint ([follow this article](https://docs.microsoft.com/en-us/azure/media-services/previous/media-services-portal-vod-get-started#start-the-streaming-endpoint)).

### 4. Deploy the Azure functions

If not already done : fork the repo, deploy Azure Functions.

Note : if you never provided your GitHub account in the Azure portal before, the continuous integration probably will probably fail and you won't see the functions. In that case, you need to setup it manually. Go to your azure functions deployment / Functions app settings / Configure continuous integration. Select GitHub as a source and configure it to use your fork.

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzure-Samples%2Fmedia-services-v3-dotnet-core-functions-integration%2Fmaster%2Fazuredeploy.json" target="_blank"><img src="http://azuredeploy.net/deploybutton.png"/></a>

## Running Functions on Local Environment

### 1. Install or Open Project with Visual Studio 2019

Install Visual Studio 2019 or open .sln file using already installed Visual Studio 2019 ([follow this article](https://visualstudio.microsoft.com/vs/)).

### 2. Make sure install Azure Development package

Please make sure you are install Azure Development package along with Visual Studio 2019. If you have not install the package, you can open Visual Studio Installer and add Azure Development package ([follow this article](https://visualstudio.microsoft.com/vs/features/azure/)).

### 3. Run the project

To try the sample locally, run the project from Visual Studio 2019. And console will open up and displaying endpoint to hit for each workflow step.
