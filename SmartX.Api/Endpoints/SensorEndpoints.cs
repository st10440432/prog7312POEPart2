//ST10440432
//Matteo Nusca
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    public static void MapSensorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sensors");

        //GET /api/sensors - list every registered sensor
        group.MapGet("/", (TelemetryStore store) => Results.Ok(store.Sensors));

        //GET /api/sensors/{deviceId} - look up one sensor
        group.MapGet("/{deviceId}", (string deviceId, TelemetryStore store) =>
        {
            var sensor = store.FindSensor(deviceId);
            return sensor is not null ? Results.Ok(sensor) : Results.NotFound();
        });

        //POST /api/sensors - register a new sensor
        group.MapPost("/", (SensorRegistration registration, TelemetryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(registration.DeviceId))
            {
                return Results.BadRequest("DeviceId is required.");
            }

            if (store.FindSensor(registration.DeviceId) is not null)
            {
                return Results.Conflict($"A sensor with DeviceId '{registration.DeviceId}' is already registered.");
            }

            store.Sensors.Add(registration);
            return Results.Created($"/api/sensors/{registration.DeviceId}", registration);
        });
    }
}