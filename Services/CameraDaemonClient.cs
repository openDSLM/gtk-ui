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
        using var response = await _httpClient.PostAsync("settings", CreateJsonContent(patch), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonSettings>(json, SerializerOptions);
    }

    public async Task<DaemonMetadataEnvelope?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("metadata", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonMetadataEnvelope>(json, SerializerOptions);
    }

    public async Task<DaemonMetadataEnvelope?> UpdateMetadataAsync(MetadataOverrides overrides, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync("metadata", CreateJsonContent(new DaemonMetadataPatch
        {
            Make = overrides.Make,
            Model = overrides.Model,
            UniqueModel = overrides.UniqueModel,
            Software = overrides.Software,
            Artist = overrides.Artist,
            Copyright = overrides.Copyright
        }), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonMetadataEnvelope>(json, SerializerOptions);
    }

    public async Task<DaemonCaptureResult?> CaptureStillAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync("capture/still", CreateJsonContent(new { }), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonCaptureResult>(json, SerializerOptions);
    }

    public async Task<DaemonStatus?> StartVideoRecordingAsync(string? directory = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "recordings/video");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var payload = new { directory };
            request.Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonStatus>(json, SerializerOptions);
    }

    public async Task<DaemonStatus?> StopVideoRecordingAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "recordings/video");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<DaemonStatus>(json, SerializerOptions);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static StringContent CreateJsonContent<T>(T payload)
    {
        return new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");
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

    [JsonPropertyName("metadata")]
    public DaemonMetadataEnvelope? Metadata { get; init; }
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
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("directory")]
    public string? Directory { get; init; }

    [JsonPropertyName("frames")]
    public string[] Frames { get; init; } = Array.Empty<string>();

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

public sealed class DaemonMetadataEnvelope
{
    [JsonPropertyName("make")]
    public string? Make { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("unique_model")]
    public string? UniqueModel { get; init; }

    [JsonPropertyName("software")]
    public string? Software { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("copyright")]
    public string? Copyright { get; init; }

    [JsonPropertyName("effective")]
    public DaemonMetadataEffective? Effective { get; init; }
}

public sealed class DaemonMetadataEffective
{
    [JsonPropertyName("make")]
    public string? Make { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("unique_model")]
    public string? UniqueModel { get; init; }

    [JsonPropertyName("software")]
    public string? Software { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("copyright")]
    public string? Copyright { get; init; }
}

public sealed record DaemonMetadataPatch
{
    [JsonPropertyName("make")]
    public string? Make { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("unique_model")]
    public string? UniqueModel { get; init; }

    [JsonPropertyName("software")]
    public string? Software { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("copyright")]
    public string? Copyright { get; init; }
}
