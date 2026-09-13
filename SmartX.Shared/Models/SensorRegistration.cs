//ST10440432
//Matteo Nusca
using System.ComponentModel.DataAnnotations;

namespace SmartX.Shared.Models;

public sealed class SensorRegistration
{
    [Required(ErrorMessage = "Device ID (MAC address or unique identifier) is required.")]
    public string DeviceId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Deployment location is required.")]
    public string DeploymentLocation { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }
    public List<string> AttachedFileNames { get; set; } = new();

    public override string ToString() => $"{DeviceId} ({Category}) @ {DeploymentLocation}";
}