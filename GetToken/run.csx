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

* How It Works
  1) The first time the GetToken function is called, it inspects the IS_FUNCTION_APP_INITIALIZED app setting. If it is "0", then
     it performs the initialization logic to create a function key whose value is already provided in the FUNCTION_APP_CUSTOM_FUNCTION_KEY
     app setting.
  2) It makes sure to change the authLevel in function.json to "function".
  3) It sets the IS_FUNCTION_APP_INITIALIZED app setting to "1" to prevent future initialization.
  4) On every subsequent call to the function, it checks if the auth level is anonymous. If it is, the call fails until someone changes
     the auth level to 'function' or 'admin'.
*/

#r "Newtonsoft.Json"

using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using Newtonsoft.Json;

const string DefaultResource = "https://management.azure.com/";
const string MsiApiVersion = "2017-09-01";

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, ExecutionContext context)
{
    log.Info("GetToken C# HTTP trigger function processed a request.");

    string functionAppFolder = context.FunctionDirectory;
    log.Info($"Function App Folder={functionAppFolder}");

    // Validate that MSI is enabled.
    string msiEndpoint = Environment.GetEnvironmentVariable("MSI_ENDPOINT");
    string msiSecret = Environment.GetEnvironmentVariable("MSI_SECRET");
    if (string.IsNullOrWhiteSpace(msiEndpoint) || string.IsNullOrWhiteSpace(msiSecret))
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("MSI is not enabled. If MSI was just enabled, make sure to restart the function before trying again.", Encoding.UTF8, "application/json")
        };
    }

    // Check is the function needs initialization:
    string isInitializedFlag = ConfigurationManager.AppSettings["IS_FUNCTION_APP_INITIALIZED"];
    if (string.IsNullOrWhiteSpace(isInitializedFlag))
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent($"Failed to initialize function. Initialization flag not found. Check the function deployment script.", Encoding.UTF8, "application/json")
        };
    }
    else if (isInitializedFlag == "0")
    {
        try
        {
            string token = await GetToken(DefaultResource, MsiApiVersion, msiEndpoint, msiSecret, log);
            string accessToken = JsonConvert.DeserializeObject<Token>(token).access_token;

            bool initialized = await Initialize(accessToken, functionAppFolder, log);
            log.Info("Initialization finished successfully.");
        }
        catch (Exception ex)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"Failed to initialize function. Exception: {ex.Message}", Encoding.UTF8, "application/json")
            };
        }
    }
    else
    {
        log.Info("Function app is already initialized.");
    }

    // Ensure that the auth level is not anonymous before proceeding. Fail if it is.
    bool isAnonymousAuthLevel = IsAnonymousFunctionAuthLevel(functionAppFolder, log);
    if (isAnonymousAuthLevel)
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Cannot run this function under 'anonymous' auth level. Change auth level to 'function' or 'admin'.", Encoding.UTF8, "application/json")
        };
    }

    // See if a resource is specified as a query parameter. If not, use default resource.
    // Examples: AMS: https://rest.media.azure.net
    string msiResource = req.GetQueryNameValuePairs().FirstOrDefault(q => string.Compare(q.Key, "msiresource", true) == 0).Value;
    msiResource = msiResource ?? DefaultResource;
    bool showDetails = req.GetQueryNameValuePairs().Any(q => string.Compare(q.Key, "showdetails", true) == 0);

    try
    {
        string token = await GetToken(msiResource, MsiApiVersion, msiEndpoint, msiSecret, log);
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
public static async Task<string> GetToken(string resource, string apiversion, string msiEndpoint, string msiSecret, TraceWriter log)
{
    string msiUrl = $"{msiEndpoint}?resource={resource}&api-version={apiversion}";
    log.Info($"MSI Endpoint={msiEndpoint}");
    //log.Info($"MSI secret={msiSecret}");
    log.Info($"MSI Url={msiUrl}");

    var headers = new Dictionary<string, string>();
    headers.Add("Secret", msiSecret);
    var tokenPayload = await InvokeRestMethodAsync(msiUrl, log, HttpMethod.Get, null, null, null, headers);
    log.Info($"Token Payload={tokenPayload}");

    return tokenPayload;
}

// For anonymous functions only:
// - When an anonymous GetToken instance is called the first time, this method is called.
// - Initializes the function app with a function key supplied in the FUNCTION_APP_CUSTOM_FUNCTION_KEY app setting and changes the auth level from anonymous to function.
// - This is used when it is desired to provide a GetToken endpoint protected by function auth level with the function code supplied by the ARM deployment template.
// - If finally sets the IS_FUNCTION_APP_INITIALIZED app setting to "1".
public static async Task<bool> Initialize(string authorizationToken, string functionAppFolder, TraceWriter log)
{
    string functionAppName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
    string functionAppHostName = $"{functionAppName}.azurewebsites.net";
    string functionAppKuduHostName = $"{functionAppName}.scm.azurewebsites.net";
    string functionName = "GetToken";
    string keyName = $"{functionName}{new Random().Next(100000, 999999)}";
    string functionKey = ConfigurationManager.AppSettings["FUNCTION_APP_CUSTOM_FUNCTION_KEY"];
    if (string.IsNullOrWhiteSpace(functionKey))
    {
        throw new Exception($"Initialization failed. Function key app setting was not found. Check the function deployment script.");
    }

    log.Info($"Function App Host Name={functionAppHostName}");
    log.Info($"Function App Kudu Host Name={functionAppKuduHostName}");
    //log.Info($"Function Key={functionKey}");

    // Get publishing credentials from the publish XML:
    string publishXmlUrl = ConfigurationManager.AppSettings["PUBLISH_XML_URL"];
    if (string.IsNullOrWhiteSpace(publishXmlUrl))
    {
        throw new Exception($"Initialization failed. Publish XML app setting was not found. Check the function deployment script.");
    }

    log.Info($"Publish XML URL={publishXmlUrl}");
    var response = await InvokeRestMethodAsync(publishXmlUrl, log, HttpMethod.Post, null, authorizationToken);
    //log.Info($"Publish Profile (XML)={response}");
    
    dynamic publishXmlAsJson = JsonConvert.DeserializeObject(XmlToJson(response));
    //log.Info($"Publish Profile (JSON)={publishXmlAsJson}");
    
    string userName = publishXmlAsJson.publishData.publishProfile[0].@userName;
    string password = publishXmlAsJson.publishData.publishProfile[0].@userPWD;
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
    {
        throw new Exception($"Initialization failed. Publish XML username or password not found.");
    }

    log.Info($"Publish XML userName={userName}");

    // Get the function app JWT auth token from admin endpoint.
    var basicAuthCode = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{userName}:{password}"));    
    string adminTokenUrl = $"https://{functionAppKuduHostName}/api/functions/admin/token";
    log.Info($"Admin Token URL={adminTokenUrl}");

    var functionAuthToken = await InvokeRestMethodAsync(adminTokenUrl, log, HttpMethod.Get, null, basicAuthCode, "Basic");
    if (string.IsNullOrWhiteSpace(functionAuthToken))
    {
        throw new Exception($"Initialization failed. Could not get the auth token from the admin endpoint.");
    }

    //log.Info($"Function Auth Token (original)={functionAuthToken}");
    var sanitizedFunctionAuthToken = functionAuthToken.Trim('"');
    //log.Info($"Function Auth Token (sanitized)={sanitizedFunctionAuthToken}");

    // Create the function key.
    string adminUrl = $"https://{functionAppHostName}/admin/functions/{functionName}/keys/{keyName}";
    log.Info($"Admin URL={adminUrl}");

    var functionKeyPayload = new FunctionKey() { name = keyName, value = functionKey };
    var jsonPayload = JsonConvert.SerializeObject(functionKeyPayload, Newtonsoft.Json.Formatting.Indented);
    //log.Info($"Create Function Key JSON Payload={jsonPayload}");

    var functionKeyCreationResponse = await InvokeRestMethodAsync(adminUrl, log, HttpMethod.Put, jsonPayload, sanitizedFunctionAuthToken);
    //log.Info($"Create Function Key Response={functionKeyCreationResponse}");

    // Set Auth Level to "Function":
    SetFunctionAuthLevelToFunction(functionAppFolder, log);

    // Set IS_FUNCTION_APP_INITIALIZED app setting to "1".
    ConfigurationManager.AppSettings["IS_FUNCTION_APP_INITIALIZED"] = "1";

    // Initialization succeeded!
    return true;
}

public static async Task<string> InvokeRestMethodAsync(string url, TraceWriter log, HttpMethod httpMethod, string body = null, string authorizationToken = null, string authorizationScheme = "Bearer", IDictionary<string, string> headers = null)
{
    HttpClient client = new HttpClient();
    if (!string.IsNullOrWhiteSpace(authorizationToken))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(authorizationScheme, authorizationToken);
        log.Info($"Authorization: {client.DefaultRequestHeaders.Authorization.Parameter}");
    }
    
    HttpRequestMessage request = new HttpRequestMessage(httpMethod, url);
    if (headers != null && headers.Count > 0)
    {
        foreach (var header in headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }
    }

    if (!string.IsNullOrWhiteSpace(body))
    {
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    }

    HttpResponseMessage response = await client.SendAsync(request);
    if (response.IsSuccessStatusCode)
    {
        return await response.Content.ReadAsStringAsync();
    }

    string statusCodeName = response.StatusCode.ToString();
    int statusCodeValue = (int)response.StatusCode;
    string content = await response.Content.ReadAsStringAsync();
    log.Info($"Status Code: {statusCodeName} ({statusCodeValue}). Body: {content}");

    throw new Exception($"Status Code: {statusCodeName} ({statusCodeValue}). Body: {content}");
}

public static string XmlToJson(string xml)
{
    XmlDocument xmlDocument = new XmlDocument();
    xmlDocument.LoadXml(xml);

    // Source: https://stackoverflow.com/questions/7278577/json-net-and-replacing-sign-in-xml-to-json-converstion
    // Process to change the default behavior of the JSON.Net converter that prefixes key names with 
    // the @ sign when calling using JsonConvert.SerializeXmlNode(xmlDocument).
    var builder = new StringBuilder();
    JsonSerializer.Create().Serialize(new CustomJsonWriter(new StringWriter(builder)), xmlDocument);
    return builder.ToString();
}

public static void SetFunctionAuthLevelToFunction(string functionAppFolder, TraceWriter log)
{
    string filePath = Path.Combine(functionAppFolder, @"function.json");
    log.Info($"function.json file path: {filePath}.");
    string functionMetadataFileContents = File.ReadAllText(filePath);
    dynamic functionMetadata = JsonConvert.DeserializeObject(functionMetadataFileContents);
    string authLevel = functionMetadata.bindings[0].authLevel;
    log.Info($"Current Auth Level: {authLevel}.");
    functionMetadata.bindings[0].authLevel = "function";
    string newFunctionMetadataFileContents = JsonConvert.SerializeObject(functionMetadata, Newtonsoft.Json.Formatting.Indented);
    File.WriteAllText(filePath, newFunctionMetadataFileContents);
}

public static bool IsAnonymousFunctionAuthLevel(string functionAppFolder, TraceWriter log)
{
    log.Info("Checking if auth level in function.json is anonymous.");
    string filePath = Path.Combine(functionAppFolder, @"function.json");
    log.Info($"function.json file path: {filePath}.");
    string functionMetadataFileContents = File.ReadAllText(filePath);
    dynamic functionMetadata = JsonConvert.DeserializeObject(functionMetadataFileContents);
    string authLevel = functionMetadata.bindings[0].authLevel;
    log.Info($"Function Auth Level: {authLevel}.");
    return (authLevel.ToLower() == "anonymous");
}

public class Token
{
    public string access_token { get; set; }
    public DateTime expires_on { get; set; }
    public string resource { get; set; }
    public string token_type { get; set; }
}

public class FunctionKey
{
    public string name;
    public string value;
}

// Source: https://stackoverflow.com/questions/7278577/json-net-and-replacing-sign-in-xml-to-json-converstion
public class CustomJsonWriter : JsonTextWriter
{
    public CustomJsonWriter(TextWriter writer): base(writer){}

    public override void WritePropertyName(string name)
    {
        if (name.StartsWith("@") || name.StartsWith("#"))
        {
            base.WritePropertyName(name.Substring(1));
        }
        else
        {
            base.WritePropertyName(name);
        }
    }
}
