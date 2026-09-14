//ST10440432
//Matteo Nusca
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class FileUploadEndpoints
{
    private static readonly string UploadsRoot = Path.Combine(AppContext.BaseDirectory, "Uploads");

    public static void MapFileUploadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sensors");

        //POST /api/sensors/{deviceId}/files - attach config file, deployment photo, hardware log
        group.MapPost("/{deviceId}/files", async (string deviceId, IFormFile file, TelemetryStore store) =>
        {
            var sensor = store.FindSensor(deviceId);
            if (sensor is null)
                return Results.NotFound($"No sensor registered with DeviceId '{deviceId}'.");

            if (file.Length == 0)
                return Results.BadRequest("Uploaded file is empty.");

            var safeDeviceFolder = string.Concat(deviceId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var sensorFolder = Path.Combine(UploadsRoot, safeDeviceFolder);
            Directory.CreateDirectory(sensorFolder);

            var safeFileName = Path.GetFileName(file.FileName); //strip any directory traversal
            var destinationPath = Path.Combine(sensorFolder, safeFileName);

            await using (var stream = new FileStream(destinationPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            sensor.AttachedFileNames.Add(safeFileName);

            return Results.Ok(new { sensor.DeviceId, UploadedFile = safeFileName });
        })
        .DisableAntiforgery();

        //GET /api/sensors/{deviceId}/files - list files attached to sensor
        group.MapGet("/{deviceId}/files", (string deviceId, TelemetryStore store) =>
        {
            var sensor = store.FindSensor(deviceId);
            return sensor is not null ? Results.Ok(sensor.AttachedFileNames) : Results.NotFound();
        });
    }
}