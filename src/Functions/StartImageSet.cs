using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Functions;

public class StartImageSet
{
    private readonly ILogger<StartImageSet> _logger;
    private static readonly QueueClientOptions _queueOptions = new QueueClientOptions
    {
        MessageEncoding = QueueMessageEncoding.Base64
    };

    public StartImageSet(ILogger<StartImageSet> logger)
    {
        _logger = logger;
    }

    [Function("StartImageSet")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        var jobId = Guid.NewGuid().ToString();
        _logger.LogInformation("StartImageSet created jobId: {jobId}", jobId);

        var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var queueClient = new QueueClient(conn, "queue-start", _queueOptions);

        await queueClient.CreateIfNotExistsAsync();
        await queueClient.SendMessageAsync(JsonSerializer.Serialize(new { jobId }));

        var res = req.CreateResponse(HttpStatusCode.Accepted);
        await res.WriteAsJsonAsync(new { jobId });
        return res;
    }
}