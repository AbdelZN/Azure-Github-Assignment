using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Functions;

public class FanOut
{
    private readonly ILogger<FanOut> _logger;
    private static readonly HttpClient _http = new HttpClient();
    private const string BuienradarUrl = "https://data.buienradar.nl/2.0/feed/json";

    public FanOut(ILogger<FanOut> logger)
    {
        _logger = logger;
    }

    [Function(nameof(FanOut))]
    [QueueOutput("queue-process", Connection = "AzureWebJobsStorage")]
    public string[] Run(
        [QueueTrigger("queue-start", Connection = "AzureWebJobsStorage")] QueueMessage message)
    {
        using var startDoc = JsonDocument.Parse(message.MessageText);
        var jobId = startDoc.RootElement.GetProperty("jobId").GetString();

        _logger.LogInformation("FanOut jobId: {jobId}", jobId);

        var json = _http.GetStringAsync(BuienradarUrl).GetAwaiter().GetResult();
        using var feedDoc = JsonDocument.Parse(json);

        var stationsArray = feedDoc.RootElement
            .GetProperty("actual")
            .GetProperty("stationmeasurements");

        var outputs = new List<string>(50);

        foreach (var station in stationsArray.EnumerateArray())
        {
            if (outputs.Count >= 50) break;

            // Known fields from your sample JSON
            var stationId = station.GetProperty("stationid").GetInt32().ToString();
            var stationName = station.GetProperty("stationname").GetString() ?? "unknown";
            var temperature = station.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : (double?)null;
            var humidity = ReadNullableIntOrString(station, "humidity");
            var windSpeed = station.TryGetProperty("windspeed", out var ws) && ws.ValueKind == JsonValueKind.Number ? ws.GetDouble() : (double?)null;
            var windDirection = station.TryGetProperty("winddirection", out var wd) && wd.ValueKind == JsonValueKind.String ? wd.GetString() : null;

            // Optional, sometimes missing
            var weatherDescription = station.TryGetProperty("weatherdescription", out var desc) && desc.ValueKind == JsonValueKind.String
                ? desc.GetString()
                : null;

            outputs.Add(JsonSerializer.Serialize(new
            {
                jobId,
                stationId,
                stationName,
                temperature,
                humidity,
                windSpeed,
                windDirection,
                weatherDescription
            }));
        }

        // Pad to 50 (because feed often has ~40 measurement stations)
        if (outputs.Count > 0 && outputs.Count < 50)
        {
            _logger.LogWarning("Buienradar returned {count} stations; padding to 50 by repeating stations.", outputs.Count);

            int needed = 50 - outputs.Count;

            for (int i = 0; i < needed; i++)
            {
                using var existingDoc = JsonDocument.Parse(outputs[i % outputs.Count]);
                var root = existingDoc.RootElement;

                var baseId = ReadNullableString(root, "stationId") ?? $"unknown-{i + 1}";
                var baseName = ReadNullableString(root, "stationName") ?? $"station-{i + 1}";

                outputs.Add(JsonSerializer.Serialize(new
                {
                    jobId,
                    stationId = $"{baseId}-dup{i + 1}",
                    stationName = $"{baseName} (dup {i + 1})",
                    temperature = ReadNullableDouble(root, "temperature"),
                    humidity = ReadNullableInt(root, "humidity"),
                    windSpeed = ReadNullableDouble(root, "windSpeed"),
                    windDirection = ReadNullableString(root, "windDirection"),
                    weatherDescription = ReadNullableString(root, "weatherDescription")
                }));
            }
        }

        _logger.LogInformation("FanOut produced {count} station jobs.", outputs.Count);
        return outputs.ToArray();
    }

    private static double? ReadNullableDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number) return el.GetDouble();
        return null;
    }

    private static int? ReadNullableInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
        return null;
    }

    private static string? ReadNullableString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        return null;
    }

    private static int? ReadNullableIntOrString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;

        if (el.ValueKind == JsonValueKind.Number)
        {
            // Some feeds represent "integer-ish" values as 95.0 etc.
            if (el.TryGetInt32(out var i)) return i;

            // Fall back to double -> int conversion
            var d = el.GetDouble();
            return (int)Math.Round(d);
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (int.TryParse(s, out var i)) return i;

            // Handle "95.0" as string too
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
                return (int)Math.Round(d);
        }

        return null;
    }


}
