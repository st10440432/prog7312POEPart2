//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Seeds a small fleet of devices, historical telemetry (with deliberate anomalies) and a believable history of
/// operator behaviour so the predictive engine has something to suggest from the first launch.
/// The behaviour history is demo data - the engine keeps learning from real searches / commands on top of it.
/// </summary>
public static class DataSeeder
{
    public const string Moisture1 = "AA:BB:CC:00:00:01";
    public const string Moisture2 = "AA:BB:CC:00:00:02";
    public const string Meter3 = "AA:BB:CC:00:00:03";
    public const string Meter4 = "AA:BB:CC:00:00:04";
    public const string MainValve = "AA:BB:CC:00:00:05";
    public const string PumpA = "AA:BB:CC:00:00:06";
    public const string PumpB = "AA:BB:CC:00:00:07";
    public const string RelayC1 = "AA:BB:CC:00:00:08";
    public const string ValveEast = "AA:BB:CC:00:00:09";

    public static void Seed(IServiceProvider services)
    {
        var registry = services.GetRequiredService<DeviceRegistry>();
        var pipeline = services.GetRequiredService<IngestionPipeline>();
        var engine = services.GetRequiredService<InsightEngine>();

        var sensors = new[]
        {
            new SensorRegistration { DeviceId = Moisture1, FriendlyName = "Moisture Sensor 1", DeploymentLocation = "Greenhouse 1, Zone A, Node 1", Category = SensorCategory.Environmental },
            new SensorRegistration { DeviceId = Moisture2, FriendlyName = "Moisture Sensor 2", DeploymentLocation = "Greenhouse 1, Zone B, Node 2", Category = SensorCategory.Environmental },
            new SensorRegistration { DeviceId = Meter3, FriendlyName = "Smart Meter 3", DeploymentLocation = "Utility Room, Zone C, Node 3", Category = SensorCategory.PowerConsumption },
            new SensorRegistration { DeviceId = Meter4, FriendlyName = "Smart Meter 4", DeploymentLocation = "Utility Room, Zone C, Node 4", Category = SensorCategory.PowerConsumption },
            new SensorRegistration { DeviceId = MainValve, FriendlyName = "Main Valve", DeploymentLocation = "Main Valve Station, Zone D, Node 5", Category = SensorCategory.Actuator },
            new SensorRegistration { DeviceId = PumpA, FriendlyName = "Pump A", DeploymentLocation = "Greenhouse 1, Zone A, Node 6", Category = SensorCategory.Actuator },
            new SensorRegistration { DeviceId = PumpB, FriendlyName = "Pump B", DeploymentLocation = "Greenhouse 1, Zone B, Node 7", Category = SensorCategory.Actuator },
            new SensorRegistration { DeviceId = RelayC1, FriendlyName = "Smart Relay C1", DeploymentLocation = "Utility Room, Zone C, Node 8", Category = SensorCategory.Actuator },
            new SensorRegistration { DeviceId = ValveEast, FriendlyName = "Valve East", DeploymentLocation = "Main Valve Station, Zone D, Node 9", Category = SensorCategory.Actuator },
        };

        foreach (var sensor in sensors)
        {
            registry.TryRegister(sensor, out _);
        }

        var now = DateTime.UtcNow;

        //Environmental: raw readings held in a jagged array (one row per device), then moved into a List<T>
        float[][] rawEnvironmentalBatches =
        {
            new float[] { 42.1f, 43.0f, 41.8f, 44.2f, 40.9f, 5.0f },   //Moisture Sensor 1: last value is a deliberate low anomaly
            new float[] { 55.3f, 54.8f, 56.1f, 53.9f, 95.0f }          //Moisture Sensor 2: last value is a deliberate high anomaly
        };
        string[] environmentalDeviceIds = { Moisture1, Moisture2 };

        var environmentalPackets = TelemetryBatchLoader.LoadHistoricalBatches(
            rawEnvironmentalBatches, environmentalDeviceIds, "Greenhouse 1", SensorCategory.Environmental,
            endUtc: now, interval: TimeSpan.FromSeconds(5));

        foreach (var packet in environmentalPackets.OrderBy(p => p.Timestamp))
        {
            pipeline.IngestImmediately(packet.DeviceId, packet.Category, packet.Value, packet.Timestamp);
        }

        //Power: Meter 3 ends on a deliberate spike (+1100 W) so a PowerSpike alert is live at startup
        SeedSeries(pipeline, Meter3, SensorCategory.PowerConsumption, new double[] { 480, 510, 495, 520, 1600 }, now);
        SeedSeries(pipeline, Meter4, SensorCategory.PowerConsumption, new double[] { 300, 310, 295, 305 }, now);

        //Actuators: on/off history (the last state becomes the commanded state)
        SeedSeries(pipeline, MainValve, SensorCategory.Actuator, new double[] { 1, 1, 0, 1 }, now, syncDesired: true);
        SeedSeries(pipeline, PumpA, SensorCategory.Actuator, new double[] { 0, 0, 0, 0 }, now, syncDesired: true);
        SeedSeries(pipeline, PumpB, SensorCategory.Actuator, new double[] { 0, 1, 1, 1 }, now, syncDesired: true);
        SeedSeries(pipeline, RelayC1, SensorCategory.Actuator, new double[] { 1, 1, 1, 1 }, now, syncDesired: true);
        SeedSeries(pipeline, ValveEast, SensorCategory.Actuator, new double[] { 1, 1, 1, 1 }, now, syncDesired: true);

        SeedOperatorBehaviour(engine, now);

        var history = services.GetRequiredService<TelemetryHistory>();
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayOptions>>().Value;
        SeedArchive(history, sensors, now.AddSeconds(-60), options.ArchiveBackfillPoints);
    }

    /// <summary>
    /// Long-term history for the AVL archive: one reading every 10 seconds going back from 'end', per device.
    /// The values are deterministic (fixed seed per device) with a few anomaly episodes mixed in, so the history
    /// page has something worth searching for. Only the archive is filled; the live timeline stays as it was.
    /// </summary>
    private static void SeedArchive(TelemetryHistory history, SensorRegistration[] sensors, DateTime end, int points)
    {
        if (points <= 0) return;

        var step = TimeSpan.FromSeconds(10);

        for (int d = 0; d < sensors.Length; d++)
        {
            var sensor = sensors[d];
            var random = new Random(1000 + d);
            var batch = new List<HistoryPoint>(points);

            for (int i = 0; i < points; i++)
            {
                DateTime stamp = end - step * (points - 1 - i);
                double value = sensor.Category switch
                {
                    SensorCategory.Environmental => 45 + 8 * Math.Sin(i / 180.0 + d) + random.NextDouble() * 2 - 1,
                    SensorCategory.PowerConsumption => 300 + d * 40 + 60 * Math.Sin(i / 90.0) + random.Next(-15, 16),
                    _ => (i / (400 + d * 37)) % 2 == 0 ? 1 : 0
                };

                //An anomaly episode roughly every 1500 readings, lasting 6 readings
                if (sensor.Category != SensorCategory.Actuator && (i + d * 211) % 1500 < 6)
                {
                    value = sensor.Category == SensorCategory.Environmental ? (d % 2 == 0 ? 4.0 : 96.0) : 1700;
                }

                batch.Add(new HistoryPoint(sensor.DeviceId, sensor.Category, sensor.DeploymentLocation,
                    Math.Round(value, 1), stamp));
            }

            history.BackfillArchive(batch);
        }
    }

    private static void SeedSeries(IngestionPipeline pipeline, string deviceId, SensorCategory category,
        double[] values, DateTime end, bool syncDesired = false)
    {
        for (int i = 0; i < values.Length; i++)
        {
            DateTime stamp = end - TimeSpan.FromSeconds(5 * (values.Length - 1 - i));
            pipeline.IngestImmediately(deviceId, category, values[i], stamp, syncDesired);
        }
    }

    /// <summary>
    /// A few hours of pretend operator habits:
    ///   Moisture Sensor 1 low  -> operator opens Pump A, then turns it on   (6 times)
    ///   Moisture Sensor 2 high -> operator turns Pump B off                 (5 of 6 times)
    ///   Smart Meter 3 spike    -> operator inspects Relay C1, switches it off (5 times)
    /// </summary>
    private static void SeedOperatorBehaviour(InsightEngine engine, DateTime now)
    {
        var events = new List<(DateTime At, Action Apply)>();

        for (int i = 0; i < 6; i++)
        {
            var t = now.AddMinutes(-200 + i * 25);
            events.Add((t, () => engine.RecordAlert(Moisture1, t)));
            events.Add((t.AddSeconds(20), () => engine.RecordSearch(PumpA, t.AddSeconds(20))));
            events.Add((t.AddSeconds(45), () => engine.RecordCommand(PumpA, true, t.AddSeconds(45))));
        }

        for (int i = 0; i < 6; i++)
        {
            var t = now.AddMinutes(-190 + i * 28);
            events.Add((t, () => engine.RecordAlert(Moisture2, t)));
            if (i != 3) //once the operator ignored it
                events.Add((t.AddSeconds(30), () => engine.RecordCommand(PumpB, false, t.AddSeconds(30))));
        }

        for (int i = 0; i < 5; i++)
        {
            var t = now.AddMinutes(-180 + i * 30);
            events.Add((t, () => engine.RecordAlert(Meter3, t)));
            events.Add((t.AddSeconds(25), () => engine.RecordSearch(RelayC1, t.AddSeconds(25))));
            events.Add((t.AddSeconds(50), () => engine.RecordCommand(RelayC1, false, t.AddSeconds(50))));
        }

        //The engine expects events in chronological order
        foreach (var e in events.OrderBy(e => e.At)) e.Apply();
    }
}
