//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;
public static class DataSeeder
{
    public static void Seed(TelemetryStore store)
    {
        var sensors = new[]
        {
            new SensorRegistration { DeviceId = "AA:BB:CC:00:00:01", DeploymentLocation = "Greenhouse 1, Zone A, Node 1", Category = SensorCategory.Environmental },
            new SensorRegistration { DeviceId = "AA:BB:CC:00:00:02", DeploymentLocation = "Greenhouse 1, Zone B, Node 2", Category = SensorCategory.Environmental },
            new SensorRegistration { DeviceId = "AA:BB:CC:00:00:03", DeploymentLocation = "Utility Room, Zone C, Node 3", Category = SensorCategory.PowerConsumption },
            new SensorRegistration { DeviceId = "AA:BB:CC:00:00:04", DeploymentLocation = "Utility Room, Zone C, Node 4", Category = SensorCategory.PowerConsumption },
            new SensorRegistration { DeviceId = "AA:BB:CC:00:00:05", DeploymentLocation = "Main Valve Station, Zone D, Node 5", Category = SensorCategory.Actuator },
        };

        store.Sensors.AddRange(sensors);

        //Environmental: seeded via the jagged array
        float[][] rawEnvironmentalBatches =
        {
            new float[] { 42.1f, 43.0f, 41.8f, 44.2f, 40.9f, 5.0f },   //Node 1: last value is a deliberate anomaly
            new float[] { 55.3f, 54.8f, 56.1f, 53.9f, 95.0f }          //Node 2: last value is a deliberate anomaly
        };
        string[] environmentalDeviceIds = { "AA:BB:CC:00:00:01", "AA:BB:CC:00:00:02" };

        var environmentalPackets = TelemetryBatchLoader.LoadHistoricalBatches(
            rawEnvironmentalBatches, environmentalDeviceIds, "Greenhouse 1", SensorCategory.Environmental);

        store.EnvironmentalReadings.AddRange(environmentalPackets);

        //Power: seeded directly, including one deliberate spike
        var baseTime = DateTime.UtcNow.AddMinutes(-10);

        int[] node3Watts = { 480, 510, 495, 1600, 520 }; //spike at index 3
        for (int i = 0; i < node3Watts.Length; i++)
        {
            store.PowerReadings.Add(new TelemetryPacket<int>(
                "AA:BB:CC:00:00:03", "Utility Room", SensorCategory.PowerConsumption,
                node3Watts[i], baseTime.AddMinutes(i)));
        }

        int[] node4Watts = { 300, 310, 295, 305 };
        for (int i = 0; i < node4Watts.Length; i++)
        {
            store.PowerReadings.Add(new TelemetryPacket<int>(
                "AA:BB:CC:00:00:04", "Utility Room", SensorCategory.PowerConsumption,
                node4Watts[i], baseTime.AddMinutes(i)));
        }

        //Actuator: a few valve on/off states
        bool[] node5States = { true, true, false, true };
        for (int i = 0; i < node5States.Length; i++)
        {
            store.ActuatorReadings.Add(new TelemetryPacket<bool>(
                "AA:BB:CC:00:00:05", "Main Valve Station", SensorCategory.Actuator,
                node5States[i], baseTime.AddMinutes(i)));
        }
    }
}