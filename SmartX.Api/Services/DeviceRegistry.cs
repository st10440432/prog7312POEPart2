//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>Mutable per-device record. Only touched while holding the registry lock.</summary>
public sealed class DeviceEntry
{
    public DeviceEntry(SensorRegistration registration, int order)
    {
        Registration = registration;
        Order = order;
    }

    public SensorRegistration Registration { get; }
    public int Order { get; }
    public DateTime? LastSeenUtc { get; set; }
    public string LastValueText { get; set; } = "-";
    public long ReadingCount { get; set; }
    public NumericTelemetryPacket<float>? LastEnvironmental { get; set; }
    public NumericTelemetryPacket<int>? LastPower { get; set; }
    public TelemetryPacket<bool>? LastActuator { get; set; }

    /// <summary>The state the gateway wants an actuator to be in (set by commands).</summary>
    public bool DesiredOn { get; set; }
}

/// <summary>Immutable copy of a device for reading outside the lock.</summary>
public sealed record DeviceView(
    SensorRegistration Registration,
    int Order,
    DateTime? LastSeenUtc,
    string LastValueText,
    long ReadingCount,
    bool? ActuatorOn,
    bool DesiredOn)
{
    public string Id => Registration.DeviceId;
    public string Name => Registration.DisplayName;
    public SensorCategory Category => Registration.Category;
}

public enum RegisterOutcome
{
    Created,
    Invalid,
    Conflict
}

/// <summary>
/// Live device registry. A Dictionary keyed by MAC / unique id gives O(1) lookup every time a telemetry
/// packet arrives (no scanning of a list). All access goes through the lock.
/// </summary>
public sealed class DeviceRegistry
{
    private readonly Dictionary<string, DeviceEntry> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) return _devices.Count; }
    }

    public RegisterOutcome TryRegister(SensorRegistration registration, out string? error)
    {
        registration.DeviceId = (registration.DeviceId ?? string.Empty).Trim();
        registration.DeploymentLocation = (registration.DeploymentLocation ?? string.Empty).Trim();
        registration.FriendlyName = string.IsNullOrWhiteSpace(registration.FriendlyName) ? null : registration.FriendlyName.Trim();

        if (string.IsNullOrWhiteSpace(registration.DeviceId))
        {
            error = "DeviceId is required.";
            return RegisterOutcome.Invalid;
        }

        if (string.IsNullOrWhiteSpace(registration.DeploymentLocation))
        {
            error = "Deployment location is required.";
            return RegisterOutcome.Invalid;
        }

        if (!Enum.IsDefined(registration.Category))
        {
            error = "Unknown sensor category.";
            return RegisterOutcome.Invalid;
        }

        //Recursive validation of the deployment hierarchy (Facility -> Zone -> Node ...)
        var zone = DeploymentZone.Parse(registration.DeploymentLocation);
        if (!zone.Validate(out var invalidPath))
        {
            error = $"Invalid deployment hierarchy at: {invalidPath}";
            return RegisterOutcome.Invalid;
        }

        lock (_gate)
        {
            if (_devices.ContainsKey(registration.DeviceId))
            {
                error = $"A sensor with DeviceId '{registration.DeviceId}' is already registered.";
                return RegisterOutcome.Conflict;
            }

            _devices[registration.DeviceId] = new DeviceEntry(registration, _devices.Count);
        }

        error = null;
        return RegisterOutcome.Created;
    }

    public bool Exists(string deviceId)
    {
        lock (_gate) return _devices.ContainsKey(deviceId.Trim());
    }

    public SensorRegistration? FindSensor(string deviceId)
    {
        lock (_gate) return _devices.TryGetValue(deviceId.Trim(), out var e) ? e.Registration : null;
    }

    /// <summary>Runs a read-only projection against the live entry under the lock.</summary>
    public T? Read<T>(string deviceId, Func<DeviceEntry, T> selector)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(deviceId.Trim(), out var e) ? selector(e) : default;
        }
    }

    /// <summary>Mutates the live entry under the lock. Returns false if the device is unknown.</summary>
    public bool Update(string deviceId, Action<DeviceEntry> mutate)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId.Trim(), out var e)) return false;
            mutate(e);
            return true;
        }
    }

    public bool AttachFile(string deviceId, string fileName)
    {
        return Update(deviceId, e => e.Registration.AttachedFileNames.Add(fileName));
    }

    public List<string>? AttachedFiles(string deviceId) =>
        Read(deviceId, e => new List<string>(e.Registration.AttachedFileNames));

    public List<SensorRegistration> Sensors()
    {
        lock (_gate)
        {
            return _devices.Values.OrderBy(e => e.Order).Select(e => e.Registration).ToList();
        }
    }

    public List<DeviceView> Views()
    {
        lock (_gate)
        {
            return _devices.Values.OrderBy(e => e.Order).Select(ToView).ToList();
        }
    }

    public DeviceView? View(string deviceId)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(deviceId.Trim(), out var e) ? ToView(e) : null;
        }
    }

    private static DeviceView ToView(DeviceEntry e) => new(
        e.Registration, e.Order, e.LastSeenUtc, e.LastValueText, e.ReadingCount,
        e.LastActuator?.Value, e.DesiredOn);

    /// <summary>Matches a free-text term against id, friendly name, location and category.</summary>
    public static bool Matches(DeviceView v, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;
        term = term.Trim();
        return v.Id.Contains(term, StringComparison.OrdinalIgnoreCase)
            || v.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || v.Registration.DeploymentLocation.Contains(term, StringComparison.OrdinalIgnoreCase)
            || v.Category.ToString().Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a search term to a single device: exact id/name match first, otherwise the only partial match.
    /// </summary>
    public DeviceView? ResolveBest(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        term = term.Trim();
        var views = Views();

        var exact = views.FirstOrDefault(v =>
            v.Id.Equals(term, StringComparison.OrdinalIgnoreCase) ||
            v.Name.Equals(term, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var partial = views.Where(v => Matches(v, term)).Take(2).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }
}
