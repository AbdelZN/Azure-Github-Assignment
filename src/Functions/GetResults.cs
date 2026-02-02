using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Azure.Storage;
using Azure.Storage.Sas;

namespace Functions;

public class GetResults
{
    private readonly ILogger<GetResults> _logger;

    public GetResults(ILogger<GetResults> logger)
    {
        _logger = logger;
    }

    [Function("GetResults")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var jobId = query["jobId"];

        var response = req.CreateResponse();

        if (string.IsNullOrWhiteSpace(jobId))
        {
            response.StatusCode = HttpStatusCode.BadRequest;
            await response.WriteStringAsync("Missing jobId. Use /api/GetResults?jobId=...");
            return response;
        }

        var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var blobService = new BlobServiceClient(conn);
        BlobContainerClient container = blobService.GetBlobContainerClient("images");

        var urls = new List<string>();
        await foreach (var item in container.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: $"{jobId}/",
            cancellationToken: default))
        {
            var blobClient = container.GetBlobClient(item.Name);
            if (conn != null && conn.Contains("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
            {
                // Local/Azurite: no SAS
                urls.Add(blobClient.Uri.ToString());
            }
            else
            {
                // Azure: return SAS URL
                urls.Add(BuildBlobReadSasUrl(blobClient, conn!, TimeSpan.FromHours(1)));
            }
        }

        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(new { jobId, results = urls });
        return response;
    }

    private static string BuildBlobReadSasUrl(BlobClient blobClient, string connectionString, TimeSpan validFor)
    {
        // Parse connection string for AccountName + AccountKey
        string? accountName = null;
        string? accountKey = null;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            if (kv[0].Equals("AccountName", StringComparison.OrdinalIgnoreCase)) accountName = kv[1];
            if (kv[0].Equals("AccountKey", StringComparison.OrdinalIgnoreCase)) accountKey = kv[1];
        }

        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(accountKey))
            throw new InvalidOperationException("AzureWebJobsStorage must be a full connection string with AccountName and AccountKey to create SAS URLs.");

        var credential = new StorageSharedKeyCredential(accountName, accountKey);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.Add(validFor)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sas = sasBuilder.ToSasQueryParameters(credential).ToString();
        return $"{blobClient.Uri}?{sas}";
    }
}