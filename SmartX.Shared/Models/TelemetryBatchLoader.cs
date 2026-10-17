//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;
public static class TelemetryBatchLoader
{
    public static List<TelemetryPacket<float>> LoadHistoricalBatches(
        float[][] rawBatches,
        string[] deviceIds,
        string zone,
        SensorCategory category,
        DateTime? endUtc = null,
        TimeSpan? interval = null)
    {
        if (rawBatches.Length != deviceIds.Length)
            throw new ArgumentException("Each device ID must correspond to exactly one raw batch.");

        var packets = new List<TelemetryPacket<float>>();
        DateTime end = endUtc ?? DateTime.UtcNow;
        TimeSpan step = interval ?? TimeSpan.FromSeconds(5);

        for (int deviceIndex = 0; deviceIndex < rawBatches.Length; deviceIndex++)
        {
            float[] deviceBatch = rawBatches[deviceIndex];
            string deviceId = deviceIds[deviceIndex];

            //Spread the batch over time 
            for (int i = 0; i < deviceBatch.Length; i++)
            {
                DateTime stamp = end - step * (deviceBatch.Length - 1 - i);
                packets.Add(new TelemetryPacket<float>(deviceId, zone, category, deviceBatch[i], stamp));
            }
        }

        return packets;
    }
}