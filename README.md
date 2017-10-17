# MSI GetToken Function App (Simple Example)
An Azure Function App to help get tokens from a Managed Service Identity (MSI) service configured on the Function App.

Two things to remember:
* This is a simple example that creates the function and provides sample URLs in the deployment's "Outputs" section for testing it.
* This function has "function" auth turned on. This means you have to first get a function key after you deploy the function app in order to be able to call it successfully. Simply append the key to the end of the URLs provided in the "Outputs" section.

[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FStratusOn%2FMSI-GetToken-FunctionApp%2FSimpleExample%2Fazuredeploy.json)
