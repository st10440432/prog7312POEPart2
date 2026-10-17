//ST10440432
//Matteo Nusca
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    private static readonly TimeSpan WaitForProcessing = TimeSpan.FromSeconds(3);

    public static void MapTelemetryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/telemetry");

        //GET /api/telemetry - newest readings per category (Part 1 dashboard)
        group.MapGet("/", (TelemetryHistory history) => Results.Ok(history.Dashboard(100)));

        //GET /api/telemetry/{deviceId}/history?minutes=30&max=200 - sorted timeline for graphs
        group.MapGet("/{deviceId}/history", (string deviceId, DeviceRegistry registry, TelemetryHistory history,
            int? minutes, int? max) =>
        {
            if (!registry.Exists(deviceId)) return Results.NotFound();

            DateTime? from = minutes is > 0 ? DateTime.UtcNow.AddMinutes(-minutes.Value) : null;
            int take = Math.Clamp(max ?? 200, 1, 500);

            var points = history.Range(deviceId, from, null, take)
                .Select(p => new TimelinePoint { TimestampUtc = p.Timestamp, Value = p.Value })
                .ToList();

            return Results.Ok(points);
        });

        //POST /api/telemetry/batch
        //every reading gets classified and placed in the FIFO or priority lane like a single packet.
        group.MapPost("/batch", (BatchIngestRequest request, DeviceRegistry registry, IngestionPipeline pipeline) =>
        {
            if (request.Readings.Count == 0) return Results.BadRequest("Batch contains no readings.");
            if (request.Readings.Count > 2000) return Results.BadRequest("A batch may contain at most 2000 readings.");

            int accepted = 0, rejected = 0;
            foreach (var reading in request.Readings)
            {
                var sensor = registry.FindSensor(reading.DeviceId ?? string.Empty);
                if (sensor is null || double.IsNaN(reading.Value) || double.IsInfinity(reading.Value))
                {
                    rejected++;
                    continue;
                }

                switch (sensor.Category)
                {
                    case SensorCategory.Environmental:
                        pipeline.EnqueueEnvironmental(sensor.DeviceId, (float)reading.Value);
                        break;
                    case SensorCategory.PowerConsumption when reading.Value >= 0:
                        pipeline.EnqueuePower(sensor.DeviceId, (int)reading.Value);
                        break;
                    case SensorCategory.Actuator:
                        pipeline.EnqueueActuator(sensor.DeviceId, reading.Value >= 0.5);
                        break;
                    default:
                        rejected++;
                        continue;
                }
                accepted++;
            }

            return Results.Accepted("/api/stream/snapshot", new BatchIngestResponse { Accepted = accepted, Rejected = rejected });
        });

        //POST /api/telemetry/environmental - float readings
        group.MapPost("/environmental", (TelemetryIngestRequest<float> request, DeviceRegistry registry, IngestionPipeline pipeline) =>
        {
            if (float.IsNaN(request.Value) || float.IsInfinity(request.Value))
                return Task.FromResult(Results.BadRequest("Environmental reading must be a finite number."));

            return Ingest(request.DeviceId, SensorCategory.Environmental, registry,
                () => pipeline.EnqueueEnvironmental(request.DeviceId, request.Value));
        });

        //POST /api/telemetry/power - int readings (watts)
        group.MapPost("/power", (TelemetryIngestRequest<int> request, DeviceRegistry registry, IngestionPipeline pipeline) =>
        {
            if (request.Value < 0)
                return Task.FromResult(Results.BadRequest("Power reading (watts) cannot be negative."));

            return Ingest(request.DeviceId, SensorCategory.PowerConsumption, registry,
                () => pipeline.EnqueuePower(request.DeviceId, request.Value));
        });

        //POST /api/telemetry/actuator - bool readings
        group.MapPost("/actuator", (TelemetryIngestRequest<bool> request, DeviceRegistry registry, IngestionPipeline pipeline) =>
            Ingest(request.DeviceId, SensorCategory.Actuator, registry,
                () => pipeline.EnqueueActuator(request.DeviceId, request.Value)));
    }

    /// <summary>
    /// Validates the device, places the packet in the right lane and (briefly) waits for the pipeline to process it,
    /// so callers get the lane + latency back. If the queue is too backed up the request is accepted (202) instead.
    /// </summary>
    private static async Task<IResult> Ingest(string deviceId, SensorCategory expected, DeviceRegistry registry, Func<IngestItem> enqueue)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Results.BadRequest("DeviceId is required.");

        var sensor = registry.FindSensor(deviceId);
        if (sensor is null)
            return Results.NotFound($"No sensor registered with DeviceId '{deviceId}'.");

        if (sensor.Category != expected)
            return Results.BadRequest($"'{sensor.DisplayName}' is a {sensor.Category} sensor and cannot send {expected} readings.");

        var item = enqueue();

        try
        {
            var processed = await item.Completion.Task.WaitAsync(WaitForProcessing);
            return Results.Created($"/api/telemetry/{sensor.DeviceId}/history", new IngestResponse
            {
                Sequence = item.Sequence,
                Lane = processed.Lane,
                Status = "processed",
                Reason = item.Reason,
                LatencyMs = processed.LatencyMs
            });
        }
        catch (TimeoutException)
        {
            return Results.Accepted($"/api/telemetry/{sensor.DeviceId}/history", new IngestResponse
            {
                Sequence = item.Sequence,
                Lane = item.Critical ? "Priority" : "FIFO",
                Status = "queued",
                Reason = item.Reason
            });
        }
    }
}
