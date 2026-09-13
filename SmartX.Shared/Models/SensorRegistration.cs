//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;
public sealed class SensorRegistration
{
    public string DeviceId { get; set; } = string.Empty; //MAC address/other unique identifier
    public string DeploymentLocation { get; set; } = string.Empty; 
    public SensorCategory Category { get; set; }
    public List<string> AttachedFileNames { get; set; } = new(); //config files, deployment photos, hardware logs

    public override string ToString() => $"{DeviceId} ({Category}) @ {DeploymentLocation}";
}