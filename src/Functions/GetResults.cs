using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

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
            urls.Add(container.GetBlobClient(item.Name).Uri.ToString());
        }

        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(new { jobId, results = urls });
        return response;
    }
}