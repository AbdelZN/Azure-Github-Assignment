using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace Functions;

public class GetStatus
{
    [Function("GetStatus")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var jobId = query["jobId"];

        var res = req.CreateResponse();

        if (string.IsNullOrWhiteSpace(jobId))
        {
            res.StatusCode = HttpStatusCode.BadRequest;
            await res.WriteStringAsync("Missing jobId. Use /api/GetStatus?jobId=...");
            return res;
        }

        var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var blobService = new BlobServiceClient(conn);
        var container = blobService.GetBlobContainerClient("images");

        int done = 0;
        await foreach (var item in container.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: $"{jobId}/",
            cancellationToken: default))
        {
            if (item.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                done++;
        }


        const int total = 50;
        var status = done >= total ? "completed" : "running";

        res.StatusCode = HttpStatusCode.OK;
        await res.WriteAsJsonAsync(new
        {
            jobId,
            status,
            total,
            done,
            failed = 0
        });

        return res;
    }
}
