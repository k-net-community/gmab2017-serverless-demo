using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using System.Net.Http.Headers;
using System.Configuration;

public async static Task Run(Stream myBlob, string name, TraceWriter log)
{       
    log.Info($"Analyzing uploaded image {name} for adult content...");

    var array = await ToByteArrayAsync(myBlob);
    var result = await AnalyzeImageAsync(array, log);
    
    log.Info("Is Adult: " + result.adult.isAdultContent.ToString());
    log.Info("Adult Score: " + result.adult.adultScore.ToString());
    log.Info("Is Racy: " + result.adult.isRacyContent.ToString());
    log.Info("Racy Score: " + result.adult.racyScore.ToString());

    var describeResult = await DescribeImageAsync(array, log);

    if (result.adult.isAdultContent || result.adult.isRacyContent)
    {
        // Copy blob to the "rejected" container
        StoreBlobWithMetadata(myBlob, "rejected", name, result, describeResult, log);
    }
    else
    {
        // Copy blob to the "accepted" container
        StoreBlobWithMetadata(myBlob, "accepted", name, result, describeResult, log);
    }
}

private async static Task<ImageAnalysisInfo> AnalyzeImageAsync(byte[] bytes, TraceWriter log)
{
    HttpClient client = new HttpClient();

    var key = ConfigurationManager.AppSettings["SubscriptionKey"].ToString();
    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", key);

    HttpContent payload = new ByteArrayContent(bytes);
    payload.Headers.ContentType = new MediaTypeWithQualityHeaderValue("application/octet-stream");
    
    var results = await client.PostAsync("https://southeastasia.api.cognitive.microsoft.com/vision/v1.0/analyze?visualFeatures=Adult", payload);
    var result = await results.Content.ReadAsAsync<ImageAnalysisInfo>();
    return result;
}

private async static Task<ImageDescriptionInfo> DescribeImageAsync(byte[] bytes, TraceWriter log)
{
    HttpClient client = new HttpClient();

    var key = ConfigurationManager.AppSettings["SubscriptionKey"].ToString();
    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", key);

    HttpContent payload = new ByteArrayContent(bytes);
    payload.Headers.ContentType = new MediaTypeWithQualityHeaderValue("application/octet-stream");
    
    var results = await client.PostAsync("https://southeastasia.api.cognitive.microsoft.com/vision/v1.0/describe", payload);
    var resultString = await results.Content.ReadAsStringAsync();
    log.Info("Describe Response: " + resultString);
    var result = await results.Content.ReadAsAsync<ImageDescriptionInfo>();
    return result;
}

// Writes a blob to a specified container and stores metadata with it
private static void StoreBlobWithMetadata(Stream image, string containerName, string blobName, ImageAnalysisInfo info, ImageDescriptionInfo desc, TraceWriter log)
{
    log.Info($"Writing blob and metadata to {containerName} container...");
    
    var connection = ConfigurationManager.AppSettings["AzureWebJobsStorage"].ToString();
    var account = CloudStorageAccount.Parse(connection);
    var client = account.CreateCloudBlobClient();
    var container = client.GetContainerReference(containerName);

    try
    {
        var blob = container.GetBlockBlobReference(blobName);
    
        if (blob != null) 
        {
            // Upload the blob
            blob.UploadFromStream(image);

            // Get the blob attributes
            blob.FetchAttributes();
            
			// Write the blob metadata
            blob.Metadata["isAdultContent"] = info.adult.isAdultContent.ToString(); 
            blob.Metadata["adultScore"] = info.adult.adultScore.ToString("P0").Replace(" ",""); 
            blob.Metadata["isRacyContent"] = info.adult.isRacyContent.ToString(); 
            blob.Metadata["racyScore"] = info.adult.racyScore.ToString("P0").Replace(" ",""); 
            blob.Metadata["description"] = desc.description.captions[0].text; 
            
			// Save the blob metadata
            blob.SetMetadata();
        }
    }
    catch (Exception ex)
    {
        log.Info(ex.Message);
    }
}

// Converts a stream to a byte array
private async static Task<byte[]> ToByteArrayAsync(Stream stream)
{
    Int32 length = stream.Length > Int32.MaxValue ? Int32.MaxValue : Convert.ToInt32(stream.Length);
    byte[] buffer = new Byte[length];
    await stream.ReadAsync(buffer, 0, length);
    stream.Position = 0;
    return buffer;
}

public class ImageAnalysisInfo
{
    public Adult adult { get; set; }
    public string requestId { get; set; }
}

public class Adult
{
    public bool isAdultContent { get; set; }
    public bool isRacyContent { get; set; }
    public float adultScore { get; set; }
    public float racyScore { get; set; }
}

public class ImageDescriptionInfo
{
    public Description description { get; set; }
    public string requestId { get; set; }
    public Metadata metadata { get; set; }
}

public class Description
{
    public string[] tags { get; set; }
    public Caption[] captions { get; set; }
}

public class Caption
{
    public string text { get; set; }
    public float confidence { get; set; }
}

public class Metadata
{
    public int width { get; set; }
    public int height { get; set; }
    public string format { get; set; }
}