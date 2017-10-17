# MSI GetToken Function App
An Azure Function App to help get tokens from a Managed Service Identity (MSI) service configured on the Function App.

[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FStratusOn%2FMSI-GetToken-FunctionApp%2Fmaster%2Fazuredeploy.json)

The deployment creates the following resources in a resource group:
* An Azure Function App primed with the GetToken function from this repo.
* A "consumption" Azure App Service Plan.
* A standard Azure Storage account.

After deploying this function app finishes, navigate to the deployment log on the Azure Portal and copy the 4 sample URLs provided in the Outputs section. You can use those URL to get a token and perform different tests:
* 2 sample tests for getting a token for use with Azure Resource Manager (ARM) resources (https://management.azure.com/). One sample returns just the access token (valid for 1 hour) and the second sample returns the full token record (access token, expiration, etc...).
* 2 sample tests for getting a token for use with Azure Media Services (AMS) resources (https://rest.media.azure.net). One sample returns just the access token (valid for 1 hour) and the second sample returns the full token record (access token, expiration, etc...).
