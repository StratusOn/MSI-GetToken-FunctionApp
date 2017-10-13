/*
* Function: GetToken
* Created: 4030 B.C.
* Created By: Ra
* Description:
  A helper function for getting an bearer access token that can be used in AAD-based API calls
  to Azure resources or Azure Resource Manager (ARM) functionality.
* Usage:
  1) Get the access token (Token string in the response body):
     https://{function-name}.azurewebsites.net/api/GetToken?code={API-code}
  2) Get the access token and other information (JSON in the response body):
     https://{function-name}.azurewebsites.net/api/GetToken?showdetails&code={API-code}

     The above 2 examples get an access token for the https://management.azure.com resource.
  3) Get the access token for a specific resource (Token string in the response body):
     https://{function-name}.azurewebsites.net/api/GetToken?msiresource=https://rest.media.azure.net&code={API-code}

     This gets an access token that can be used for Azure Media Services (AMS) API calls.

* Background:
  To enable MSI on the Function App:
  https://docs.microsoft.com/en-us/azure/app-service/app-service-managed-service-identity
*/

#r "Newtonsoft.Json"

using System.Net;
using System.Text;
using Newtonsoft.Json;

const string DefaultResource = "https://management.azure.com";
const string MsiApiVersion = "2017-09-01";

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info("GetToken C# HTTP trigger function processed a request.");

    // Validate that MSI is enabled.
    string msiendpoint = Environment.GetEnvironmentVariable("MSI_ENDPOINT");
    string msiSecret = Environment.GetEnvironmentVariable("MSI_SECRET");
    if (string.IsNullOrWhiteSpace(msiendpoint) || string.IsNullOrWhiteSpace(msiSecret))
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("MSI is not enabled. If MSI was just enabled, make sure to restart the function before trying again.", Encoding.UTF8, "application/json")
        };
    }

    // See if a resource is specified as a query parameter. If not, use default resource.
    // Examples: AMS: https://rest.media.azure.net
    string msiResource = req.GetQueryNameValuePairs().FirstOrDefault(q => string.Compare(q.Key, "msiresource", true) == 0).Value;
    msiResource = msiResource ?? DefaultResource;
    bool showDetails = req.GetQueryNameValuePairs().Any(q => string.Compare(q.Key, "showdetails", true) == 0);

    try
    {
        string token = await GetToken(msiResource, MsiApiVersion, msiendpoint, msiSecret, log);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Failed to get a token.", Encoding.UTF8, "application/json")
            };
        }
        else
        {
            HttpContent content = null;
            if (showDetails)
            {
                content = new StringContent(token, Encoding.UTF8, "application/json");
            }
            else
            {
                string accessToken = JsonConvert.DeserializeObject<Token>(token).access_token;
                content = new StringContent(accessToken, Encoding.UTF8, "application/json");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            };
        }
    }
    catch (Exception ex)
    {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"Failed to get a token. Exception: {ex.Message}", Encoding.UTF8, "application/json")
            };
    }
}

// Returns a JSON string of the form (see Token class definition):
// {"access_token":"eyJ0...s1DZw","expires_on":"12/12/2017 10:20:00 AM +00:00","resource":"https://management.azure.com","token_type":"Bearer"}
// Bearer tokens returned are typically valid for only 1 hour.
public static async Task<string> GetToken(string resource, string apiversion, string msiendpoint, string msiSecret, TraceWriter log)  {
    HttpClient client = new HttpClient();
    client.DefaultRequestHeaders.Add("Secret", msiSecret);
    string msiUrl = String.Format("{0}?resource={1}&api-version={2}", msiendpoint, resource, apiversion);

    log.Info($"MSI Endpoint={msiendpoint}");
    //log.Info($"MSI secret={msiSecret}");
    log.Info($"MSI Url={msiUrl}");

    var response = await client.GetAsync(msiUrl);
    var tokenPayload = await response.Content.ReadAsStringAsync();

    log.Info($"Token Payload={tokenPayload}");

    return tokenPayload;
}

public class Token
{
    public string access_token { get; set; }
    public DateTime expires_on { get; set; }
    public string resource { get; set; }
    public string token_type { get; set; }
}