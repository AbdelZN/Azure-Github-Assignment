using Azure.Storage.Blobs;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;


namespace Functions;

public class ProcessStation
{
    private readonly ILogger<ProcessStation> _logger;
    private static readonly HttpClient _http = new HttpClient();

    public ProcessStation(ILogger<ProcessStation> logger)
    {
        _logger = logger;
    }

    [Function(nameof(ProcessStation))]
    public async Task Run(
        [QueueTrigger("queue-process", Connection = "AzureWebJobsStorage")] QueueMessage message)
    {
        using var doc = JsonDocument.Parse(message.MessageText);
        var root = doc.RootElement;

        // jobId is required, but don't crash if it's missing
        string jobId = root.TryGetProperty("jobId", out var jobIdEl) && jobIdEl.ValueKind == JsonValueKind.String
            ? jobIdEl.GetString()!
            : "unknown-job";

        // stationId might be missing if some old messages are still in the queue
        string stationId = root.TryGetProperty("stationId", out var stationIdEl) && stationIdEl.ValueKind == JsonValueKind.String
            ? stationIdEl.GetString()!
            : (root.TryGetProperty("stationIndex", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number
                ? $"index-{idxEl.GetInt32()}"
                : $"msg-{message.MessageId}");

        _logger.LogInformation("ProcessStation received jobId: {jobId}", jobId);

        var stationName = root.TryGetProperty("stationName", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString() : "";
        var temperature = root.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : (double?)null;
        var humidity = root.TryGetProperty("humidity", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : (int?)null;
        var windSpeed = root.TryGetProperty("windSpeed", out var ws) && ws.ValueKind == JsonValueKind.Number ? ws.GetDouble() : (double?)null;
        var windDirection = root.TryGetProperty("windDirection", out var wd) && wd.ValueKind == JsonValueKind.String ? wd.GetString() : null;
        var weatherDescription = root.TryGetProperty("weatherDescription", out var desc) && desc.ValueKind == JsonValueKind.String ? desc.GetString() : null;


        // Blob setup (Azurite uses AzureWebJobsStorage from local.settings.json)
        var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var blobService = new BlobServiceClient(conn);
        var container = blobService.GetBlobContainerClient("images");
        await container.CreateIfNotExistsAsync();

        var imageUrl = "https://picsum.photos/800/500";
        var imageBytes = await _http.GetByteArrayAsync(imageUrl);

        // Build overlay text
        var lines = new List<string>
        {
            $"{stationName} ({stationId})",
            weatherDescription ?? "",
            temperature is not null ? $"Temp: {temperature:0.0}°C" : "Temp: -",
            humidity is not null ? $"Humidity: {humidity}%" : "Humidity: -",
            windSpeed is not null ? $"Wind: {windSpeed:0.0} m/s {windDirection ?? ""}".Trim() : $"Wind: - {windDirection ?? ""}".Trim()
        };

        var overlayText = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));

        // Edit image in memory
        using var image = Image.Load(imageBytes);

        // Choose a font (system fonts may be limited, so use a built-in fallback)
        Font font;
        try
        {
            font = SystemFonts.CreateFont("Arial", 24);
        }
        catch
        {
            font = SystemFonts.Families.First().CreateFont(24);
        }

        image.Mutate(ctx =>
        {
            // Draw a simple black shadow + white text for readability
            ctx.DrawText(overlayText, font, Color.Black, new PointF(21, 21));
            ctx.DrawText(overlayText, font, Color.White, new PointF(20, 20));
        });

        // Encode to JPEG bytes
        await using var outStream = new MemoryStream();
        await image.SaveAsync(outStream, new JpegEncoder { Quality = 85 });
        outStream.Position = 0;

        // Upload edited image
        var blobName = $"{jobId}/station-{stationId}.jpg";
        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(outStream, overwrite: true);
        _logger.LogInformation("Wrote image blob with overlay: images/{blobName}", blobName);
    }
}