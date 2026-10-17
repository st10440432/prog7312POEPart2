//ST10440432
//Matteo Nusca
using System.Net.Http.Json;
using SmartX.Shared.Models;

// Smart-X device simulator.
//AI WAS USED TO HELP GENERATE THIS TOOL.
// Behaves like a fleet of ESP32 nodes: registers itself with the gateway, then POSTs telemetry over HTTP.
//
//   dotnet run --project SmartX.Simulator -- [scenario] [--api URL] [--interval ms] [--extra N] [--count N]
//
// Scenarios:
//   normal         (default) continuous healthy telemetry from the whole fleet
//   demo           normal + a random anomaly every ~20 seconds
//   power-spike    one +1400 W spike on Smart Meter 3, then exit
//   moisture-drop  Moisture Sensor 1 reports a severe low reading, then exit
//   disconnect     normal, but Smart Meter 4 and Pump B go silent (watchdog raises Disconnected)
//   flood          burst of --count (default 300) normal packets, then a spike while the backlog drains

var options = SimOptions.Parse(args);
using var http = new HttpClient { BaseAddress = new Uri(options.ApiUrl), Timeout = TimeSpan.FromSeconds(20) };
var rng = new Random();

Console.WriteLine($"Smart-X simulator -> {options.ApiUrl}  scenario={options.Scenario}");

var fleet = Fleet.Build(options.ExtraDevices);
await WaitForApi(http);
await Register(http, fleet);

switch (options.Scenario)
{
    case "power-spike":
        await Post(http, fleet.First(d => d.Id == Fleet.Meter3), 1900);
        break;

    case "moisture-drop":
        await Post(http, fleet.First(d => d.Id == Fleet.Moisture1), 6);
        break;

    case "flood":
        await RunFlood(http, fleet, options.Count, rng);
        break;

    default:
        await RunContinuous(http, fleet, options, rng);
        break;
}

return 0;

// ------------------------------------------------------------------------------------------------

static async Task WaitForApi(HttpClient http)
{
    for (int attempt = 1; ; attempt++)
    {
        try
        {
            using var response = await http.GetAsync("/");
            if (response.IsSuccessStatusCode) return;
        }
        catch (HttpRequestException)
        {
        }

        Console.WriteLine($"Waiting for the gateway API ({attempt})...");
        await Task.Delay(2000);
    }
}

static async Task Register(HttpClient http, List<SimDevice> fleet)
{
    int created = 0, existing = 0;
    foreach (var d in fleet)
    {
        var response = await http.PostAsJsonAsync("/api/sensors", new SensorRegistration
        {
            DeviceId = d.Id,
            FriendlyName = d.Name,
            DeploymentLocation = d.Location,
            Category = d.Category
        });

        if (response.IsSuccessStatusCode) created++;
        else if ((int)response.StatusCode == 409) existing++;
        else Console.WriteLine($"  could not register {d.Name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    Console.WriteLine($"Fleet ready: {fleet.Count} devices ({created} newly registered, {existing} already known).");
}

static async Task<(bool Ok, IngestResponse? Body)> Post(HttpClient http, SimDevice d, double value)
{
    try
    {
        HttpResponseMessage response = d.Category switch
        {
            SensorCategory.Environmental => await http.PostAsJsonAsync("/api/telemetry/environmental",
                new TelemetryIngestRequest<float>(d.Id, (float)value)),
            SensorCategory.PowerConsumption => await http.PostAsJsonAsync("/api/telemetry/power",
                new TelemetryIngestRequest<int>(d.Id, (int)value)),
            _ => await http.PostAsJsonAsync("/api/telemetry/actuator",
                new TelemetryIngestRequest<bool>(d.Id, value >= 0.5))
        };

        IngestResponse? body = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IngestResponse>()
            : null;

        if (!response.IsSuccessStatusCode)
            Console.WriteLine($"  {d.Name}: HTTP {(int)response.StatusCode}");
        else if (body is { Lane: "Priority" })
            Console.WriteLine($"  !! {d.Name} = {value:0.#}  -> PRIORITY lane ({body.Reason}), {body.LatencyMs} ms");

        return (response.IsSuccessStatusCode, body);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"  {d.Name}: {ex.Message}");
        return (false, null);
    }
}

static async Task RunContinuous(HttpClient http, List<SimDevice> fleet, SimOptions options, Random rng)
{
    var silent = options.Scenario == "disconnect"
        ? new HashSet<string> { Fleet.Meter4, Fleet.PumpB }
        : new HashSet<string>();

    var nextAnomaly = DateTime.UtcNow.AddSeconds(20);
    long tick = 0;
    Console.WriteLine("Sending telemetry. Press Ctrl+C to stop.");

    while (true)
    {
        tick++;
        var desired = await GetDesiredStates(http);

        var sends = new List<Task<(bool Ok, IngestResponse? Body)>>();
        foreach (var d in fleet)
        {
            if (silent.Contains(d.Id)) continue;

            double value;
            if (d.Category == SensorCategory.Environmental)
                value = d.NextEnvironmental(rng);
            else if (d.Category == SensorCategory.PowerConsumption)
                value = d.NextPower(rng);
            else //actuators report whatever state the gateway last commanded
                value = desired.TryGetValue(d.Id, out var on) ? (on ? 1 : 0) : d.LastValue;

            d.LastValue = value;
            sends.Add(Post(http, d, value));
        }

        if (options.Scenario == "demo" && DateTime.UtcNow >= nextAnomaly)
        {
            nextAnomaly = DateTime.UtcNow.AddSeconds(15 + rng.Next(15));
            await InjectRandomAnomaly(http, fleet, rng);
        }

        var results = await Task.WhenAll(sends);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] tick {tick}: {results.Count(r => r.Ok)}/{results.Length} packets accepted");
        await Task.Delay(options.IntervalMs);
    }
}

static async Task InjectRandomAnomaly(HttpClient http, List<SimDevice> fleet, Random rng)
{
    var env = fleet.Where(d => d.Category == SensorCategory.Environmental).ToList();
    var power = fleet.Where(d => d.Category == SensorCategory.PowerConsumption).ToList();

    if (rng.Next(2) == 0)
    {
        var d = env[rng.Next(env.Count)];
        Console.WriteLine($"** demo anomaly: {d.Name} reports out-of-range");
        d.LastValue = rng.Next(2) == 0 ? 6 : 96;
        await Post(http, d, d.LastValue);
    }
    else
    {
        var d = power[rng.Next(power.Count)];
        Console.WriteLine($"** demo anomaly: {d.Name} power spike");
        d.LastValue += 1200;
        await Post(http, d, d.LastValue);
    }
}

static async Task RunFlood(HttpClient http, List<SimDevice> fleet, int count, Random rng)
{
    var env = fleet.Where(d => d.Category == SensorCategory.Environmental).ToList();
    var meter = fleet.First(d => d.Id == Fleet.Meter4);

    await Post(http, meter, 300); //baseline so the spike is measurable
    Console.WriteLine($"Flooding the gateway with {count} normal packets...");

    var flood = Enumerable.Range(0, count)
        .Select(i => Post(http, env[i % env.Count], 40 + rng.Next(20)))
        .ToList();

    await Task.Delay(400);
    Console.WriteLine("Backlog is building - now injecting a critical power spike.");
    var spike = await Post(http, meter, 1800);

    var normal = (await Task.WhenAll(flood))
        .Where(r => r.Body is { Lane: "FIFO" })
        .Select(r => r.Body!.LatencyMs)
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"Critical packet : lane={spike.Body?.Lane}, latency={spike.Body?.LatencyMs} ms");
    if (normal.Count > 0)
        Console.WriteLine($"Normal packets  : lane=FIFO, avg latency={normal.Average():0} ms, max={normal.Max():0} ms ({normal.Count} processed)");
    Console.WriteLine("The critical packet bypassed the FIFO backlog.");
}

static async Task<Dictionary<string, bool>> GetDesiredStates(HttpClient http)
{
    try
    {
        return await http.GetFromJsonAsync<Dictionary<string, bool>>("/api/commands/desired-state")
               ?? new Dictionary<string, bool>();
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return new Dictionary<string, bool>();
    }
}

// ------------------------------------------------------------------------------------------------

sealed record SimOptions(string ApiUrl, string Scenario, int IntervalMs, int ExtraDevices, int Count)
{
    public static SimOptions Parse(string[] args)
    {
        string api = Environment.GetEnvironmentVariable("SIM_API_URL") ?? "http://localhost:5248";
        string scenario = Environment.GetEnvironmentVariable("SIM_SCENARIO") ?? "normal";
        int interval = 1000, extra = 12, count = 300;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--api" when i + 1 < args.Length: api = args[++i]; break;
                case "--interval" when i + 1 < args.Length: interval = int.Parse(args[++i]); break;
                case "--extra" when i + 1 < args.Length: extra = int.Parse(args[++i]); break;
                case "--count" when i + 1 < args.Length: count = int.Parse(args[++i]); break;
                default:
                    if (!args[i].StartsWith("--")) scenario = args[i].ToLowerInvariant();
                    break;
            }
        }

        return new SimOptions(api.TrimEnd('/'), scenario, Math.Max(100, interval), Math.Max(0, extra), Math.Max(1, count));
    }
}

sealed class SimDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public required SensorCategory Category { get; init; }
    public double LastValue { get; set; }

    public double NextEnvironmental(Random rng)
    {
        if (LastValue is < 20 or > 75) LastValue = 40 + rng.Next(15); //recover from an injected anomaly
        LastValue = Math.Clamp(LastValue + (rng.NextDouble() - 0.5) * 3, 25, 70);
        return Math.Round(LastValue, 1);
    }

    public double NextPower(Random rng)
    {
        if (LastValue > 900 || LastValue < 100) LastValue = 300 + rng.Next(200);
        LastValue = Math.Clamp(LastValue + rng.Next(-40, 41), 150, 800);
        return Math.Round(LastValue);
    }
}

static class Fleet
{
    public const string Moisture1 = "AA:BB:CC:00:00:01";
    public const string Meter3 = "AA:BB:CC:00:00:03";
    public const string Meter4 = "AA:BB:CC:00:00:04";
    public const string PumpB = "AA:BB:CC:00:00:07";

    public static List<SimDevice> Build(int extra)
    {
        static SimDevice D(string id, string name, string loc, SensorCategory c, double start = 0) =>
            new() { Id = id, Name = name, Location = loc, Category = c, LastValue = start };

        var fleet = new List<SimDevice>
        {
            D("AA:BB:CC:00:00:01", "Moisture Sensor 1", "Greenhouse 1, Zone A, Node 1", SensorCategory.Environmental, 42),
            D("AA:BB:CC:00:00:02", "Moisture Sensor 2", "Greenhouse 1, Zone B, Node 2", SensorCategory.Environmental, 55),
            D("AA:BB:CC:00:00:03", "Smart Meter 3", "Utility Room, Zone C, Node 3", SensorCategory.PowerConsumption, 500),
            D("AA:BB:CC:00:00:04", "Smart Meter 4", "Utility Room, Zone C, Node 4", SensorCategory.PowerConsumption, 300),
            D("AA:BB:CC:00:00:05", "Main Valve", "Main Valve Station, Zone D, Node 5", SensorCategory.Actuator),
            D("AA:BB:CC:00:00:06", "Pump A", "Greenhouse 1, Zone A, Node 6", SensorCategory.Actuator),
            D("AA:BB:CC:00:00:07", "Pump B", "Greenhouse 1, Zone B, Node 7", SensorCategory.Actuator),
            D("AA:BB:CC:00:00:08", "Smart Relay C1", "Utility Room, Zone C, Node 8", SensorCategory.Actuator),
            D("AA:BB:CC:00:00:09", "Valve East", "Main Valve Station, Zone D, Node 9", SensorCategory.Actuator),
        };

        //Extra devices spread over a second greenhouse and a workshop so the hierarchy has some depth
        for (int i = 1; i <= extra; i++)
        {
            string id = $"5E:00:00:00:00:{i:X2}";
            string zone = i % 2 == 0 ? "Zone A" : "Zone B";
            switch (i % 3)
            {
                case 1:
                    fleet.Add(D(id, $"Soil Probe G2-{i}", $"Greenhouse 2, {zone}, Node {10 + i}", SensorCategory.Environmental, 35 + i));
                    break;
                case 2:
                    fleet.Add(D(id, $"Smart Plug W{i}", $"Workshop, {zone}, Node {10 + i}", SensorCategory.PowerConsumption, 250 + i * 10));
                    break;
                default:
                    fleet.Add(D(id, $"Irrigation Valve G2-{i}", $"Greenhouse 2, {zone}, Node {10 + i}", SensorCategory.Actuator));
                    break;
            }
        }

        return fleet;
    }
}
