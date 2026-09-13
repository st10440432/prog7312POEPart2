//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;
public sealed record TelemetryIngestRequest<T>(string DeviceId, T Value) where T : struct;