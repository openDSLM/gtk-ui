using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

public sealed class CameraDaemonClient : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public CameraDaemonClient(Uri baseAddress)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = baseAddress ?? throw new ArgumentNullException(nameof(baseAddress))
        };
    }

    public async Task<DaemonStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("status", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonStatus>(json, SerializerOptions);
    }

    public async Task<DaemonSettings?> UpdateSettingsAsync(DaemonSettingsPatch patch, CancellationToken cancellationToken = default)
    {
        var content = new StringContent(JsonSerializer.Serialize(patch, SerializerOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("settings", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonSettings>(json, SerializerOptions);
    }

    public async Task<DaemonCaptureResult?> CaptureStillAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync("capture/still", new StringContent("{}", Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonCaptureResult>(json, SerializerOptions);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public sealed class DaemonStatus
{
    [JsonPropertyName("state")]
    public DaemonSessionState? State { get; init; }

    [JsonPropertyName("settings")]
    public DaemonSettings? Settings { get; init; }

    [JsonPropertyName("preview_pipeline")]
    public string? PreviewPipeline { get; init; }

    [JsonPropertyName("preview_client_pipeline")]
    public string? PreviewClientPipeline { get; init; }

    [JsonPropertyName("last_capture")]
    public DaemonCaptureResult? LastCapture { get; init; }
}

public sealed class DaemonSessionState
{
    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }
}

public sealed class DaemonSettings
{
    [JsonPropertyName("fps")]
    public double Fps { get; init; }

    [JsonPropertyName("shutter_us")]
    public double ShutterUs { get; init; }

    [JsonPropertyName("analogue_gain")]
    public double AnalogueGain { get; init; }

    [JsonPropertyName("auto_exposure")]
    public bool AutoExposure { get; init; }

    [JsonPropertyName("output_dir")]
    public string? OutputDir { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }
}

public sealed record DaemonSettingsPatch
{
    [JsonPropertyName("fps")]
    public double? Fps { get; init; }

    [JsonPropertyName("shutter_us")]
    public double? ShutterUs { get; init; }

    [JsonPropertyName("analogue_gain")]
    public double? AnalogueGain { get; init; }

    [JsonPropertyName("auto_exposure")]
    public bool? AutoExposure { get; init; }

    [JsonPropertyName("output_dir")]
    public string? OutputDir { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }
}

public sealed class DaemonCaptureResult
{
    [JsonPropertyName("frames")]
    public string[] Frames { get; init; } = Array.Empty<string>();

    [JsonPropertyName("count")]
    public int Count { get; init; }
}
