//ST10440432
//Matteo Nusca
using System.Net.Http.Json;
using System.Text.Json;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

public sealed record ApiResult<T>(bool Ok, T? Data, string? Error, int Status)
{
    public static ApiResult<T> Success(T? data, int status = 200) => new(true, data, null, status);
    public static ApiResult<T> Failure(string error, int status = 0) => new(false, default, error, status);
}

/// <summary>
/// Typed wrapper around the gateway API. Every call is asynchronous and returns an ApiResult instead of throwing,
/// so a stopped / unreachable API shows a banner in the UI rather than crashing the page.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    // ---- reads

    public Task<ApiResult<StreamSnapshot>> GetSnapshotAsync(string? search, SensorCategory? category, bool alertsOnly, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (category is not null) query.Add($"category={(int)category.Value}");
        if (alertsOnly) query.Add("alertsOnly=true");

        string url = "api/stream/snapshot" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return GetAsync<StreamSnapshot>(url, ct);
    }

    public Task<ApiResult<List<TimelinePoint>>> GetTimelineAsync(string deviceId, CancellationToken ct = default) =>
        GetAsync<List<TimelinePoint>>($"api/telemetry/{Uri.EscapeDataString(deviceId)}/history?max=120", ct);

    public Task<ApiResult<ZoneNodeDto>> GetZonesAsync(CancellationToken ct = default) =>
        GetAsync<ZoneNodeDto>("api/zones", ct);

    public Task<ApiResult<List<SensorRegistration>>> GetSensorsAsync(CancellationToken ct = default) =>
        GetAsync<List<SensorRegistration>>("api/sensors", ct);

    public Task<ApiResult<TelemetryDashboardResponse>> GetTelemetryAsync(CancellationToken ct = default) =>
        GetAsync<TelemetryDashboardResponse>("api/telemetry", ct);

    // ---- commands

    public Task<ApiResult<CommandRecord>> ExecuteAsync(CommandRequest request) =>
        PostAsync<CommandRecord>("api/commands", request);

    public Task<ApiResult<CommandRecord>> ExecuteBulkAsync(BulkCommandRequest request) =>
        PostAsync<CommandRecord>("api/commands/bulk", request);

    public Task<ApiResult<CommandRecord>> UndoAsync() =>
        PostAsync<CommandRecord>("api/commands/undo", new { });

    // ---- learning

    public Task<ApiResult<SearchEventResponse>> LogSearchAsync(string term) =>
        PostAsync<SearchEventResponse>("api/insights/events", new SearchEventRequest { Term = term });

    public Task<ApiResult<object>> SendFeedbackAsync(string suggestionId, bool accepted) =>
        PostAsync<object>("api/insights/feedback", new FeedbackRequest { SuggestionId = suggestionId, Accepted = accepted });

    // ---- telemetry (the dashboard's simulation tools act like a device)

    public Task<ApiResult<IngestResponse>> PostEnvironmentalAsync(string deviceId, float value) =>
        PostAsync<IngestResponse>("api/telemetry/environmental", new TelemetryIngestRequest<float>(deviceId, value));

    public Task<ApiResult<IngestResponse>> PostPowerAsync(string deviceId, int value) =>
        PostAsync<IngestResponse>("api/telemetry/power", new TelemetryIngestRequest<int>(deviceId, value));

    public Task<ApiResult<IngestResponse>> PostActuatorAsync(string deviceId, bool value) =>
        PostAsync<IngestResponse>("api/telemetry/actuator", new TelemetryIngestRequest<bool>(deviceId, value));

    public Task<ApiResult<BatchIngestResponse>> PostBatchAsync(List<BatchReading> readings) =>
        PostAsync<BatchIngestResponse>("api/telemetry/batch", new BatchIngestRequest { Readings = readings });

    // ---- plumbing

    private async Task<ApiResult<T>> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Failure(await ReadError(response), (int)response.StatusCode);

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return ApiResult<T>.Success(data, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return ApiResult<T>.Failure(Describe(ex));
        }
    }

    private async Task<ApiResult<T>> PostAsync<T>(string url, object body)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(url, body);
            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Failure(await ReadError(response), (int)response.StatusCode);

            T? data = default;
            if (response.Content.Headers.ContentLength != 0 && typeof(T) != typeof(object))
            {
                try { data = await response.Content.ReadFromJsonAsync<T>(); }
                catch (JsonException) { /* empty or non-JSON body is fine */ }
            }

            return ApiResult<T>.Success(data, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return ApiResult<T>.Failure(Describe(ex));
        }
    }

    private static async Task<string> ReadError(HttpResponseMessage response)
    {
        string text = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
        return string.IsNullOrWhiteSpace(text) ? $"The gateway returned HTTP {(int)response.StatusCode}." : text;
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException => "Cannot reach the gateway API. Is SmartX.Api running?",
        TaskCanceledException => "The gateway API took too long to respond.",
        _ => $"Unexpected response from the gateway: {ex.Message}"
    };
}
