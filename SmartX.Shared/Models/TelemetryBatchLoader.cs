//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;
public static class TelemetryBatchLoader
{
    public static List<TelemetryPacket<float>> LoadHistoricalBatches(
        float[][] rawBatches,
        string[] deviceIds,
        string zone,
        SensorCategory category)
    {
        if (rawBatches.Length != deviceIds.Length)
            throw new ArgumentException("Each device ID must correspond to exactly one raw batch.");

        var packets = new List<TelemetryPacket<float>>();

        for (int deviceIndex = 0; deviceIndex < rawBatches.Length; deviceIndex++)
        {
            float[] deviceBatch = rawBatches[deviceIndex];
            string deviceId = deviceIds[deviceIndex];

            foreach (float reading in deviceBatch)
            {
                packets.Add(new TelemetryPacket<float>(deviceId, zone, category, reading));
            }
        }

        return packets;
    }
}