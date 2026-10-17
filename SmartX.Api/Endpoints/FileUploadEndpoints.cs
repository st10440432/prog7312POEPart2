//ST10440432
//Matteo Nusca
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class FileUploadEndpoints
{
    private static readonly string UploadsRoot = Path.Combine(AppContext.BaseDirectory, "Uploads");
    private const long MaxFileBytes = 10 * 1024 * 1024; //10 MB

    public static void MapFileUploadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sensors");

        //POST /api/sensors/{deviceId}/files - attach config file, deployment photo, hardware log
        group.MapPost("/{deviceId}/files", async (string deviceId, IFormFile file, DeviceRegistry registry) =>
        {
            var sensor = registry.FindSensor(deviceId);
            if (sensor is null)
                return Results.NotFound($"No sensor registered with DeviceId '{deviceId}'.");

            if (file.Length == 0)
                return Results.BadRequest("Uploaded file is empty.");

            if (file.Length > MaxFileBytes)
                return Results.BadRequest("File is larger than the 10 MB limit.");

            var safeDeviceFolder = string.Concat(sensor.DeviceId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var sensorFolder = Path.Combine(UploadsRoot, safeDeviceFolder);
            Directory.CreateDirectory(sensorFolder);

            var safeFileName = Path.GetFileName(file.FileName); //strip any directory traversal
            var destinationPath = Path.Combine(sensorFolder, safeFileName);

            await using (var stream = new FileStream(destinationPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            registry.AttachFile(sensor.DeviceId, safeFileName);

            return Results.Ok(new { sensor.DeviceId, UploadedFile = safeFileName });
        })
        .DisableAntiforgery();

        //GET /api/sensors/{deviceId}/files - list files attached to sensor
        group.MapGet("/{deviceId}/files", (string deviceId, DeviceRegistry registry) =>
        {
            var files = registry.AttachedFiles(deviceId);
            return files is not null ? Results.Ok(files) : Results.NotFound();
        });
    }
}
