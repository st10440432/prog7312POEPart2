//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public enum CommandOutcome
{
    Ok,
    NotFound,
    Invalid,
    NoChange
}

public sealed record CommandResult(CommandOutcome Outcome, string Message, CommandRecord? Record = null);

/// <summary>
/// Manual override commands (pump / valve / relay on-off) with an Undo history.
/// Every executed command is pushed onto a Stack&lt;T&gt;; Undo pops the newest command and restores the
/// exact previous state of every device it touched (so "shut down all valves" is undone in one step).
/// </summary>
public sealed class CommandService
{
    private sealed class ExecutedCommand
    {
        public required CommandRecord Record { get; init; }
        public required Dictionary<string, bool> PreviousStates { get; init; }
    }

    private readonly Stack<ExecutedCommand> _undoStack = new();
    private readonly List<CommandRecord> _history = new();
    private readonly object _gate = new();
    private readonly DeviceRegistry _registry;
    private readonly IngestionPipeline _pipeline;
    private readonly InsightEngine _engine;
    private long _nextId;

    public CommandService(DeviceRegistry registry, IngestionPipeline pipeline, InsightEngine engine)
    {
        _registry = registry;
        _pipeline = pipeline;
        _engine = engine;
    }

    public int UndoDepth
    {
        get { lock (_gate) return _undoStack.Count; }
    }

    public CommandResult Execute(CommandRequest request)
    {
        var device = _registry.View(request.DeviceId ?? string.Empty);
        if (device is null)
            return new CommandResult(CommandOutcome.NotFound, $"No sensor registered with DeviceId '{request.DeviceId}'.");

        if (device.Category != SensorCategory.Actuator)
            return new CommandResult(CommandOutcome.Invalid, $"'{device.Name}' is not an actuator and cannot be commanded.");

        bool target = ResolveTarget(request.Action, device.DesiredOn);
        if (target == device.DesiredOn)
            return new CommandResult(CommandOutcome.NoChange, $"{device.Name} is already {(target ? "ON" : "OFF")}.");

        var previous = new Dictionary<string, bool> { [device.Id] = device.DesiredOn };
        string description = $"{(target ? "Turn ON" : "Turn OFF")} {device.Name}";

        return Apply(description, target, new List<string> { device.Id }, previous, request.Action, request.Source);
    }

    public CommandResult ExecuteBulk(BulkCommandRequest request)
    {
        string filter = (request.Filter ?? string.Empty).Trim();

        var targets = _registry.Views()
            .Where(v => v.Category == SensorCategory.Actuator && DeviceRegistry.Matches(v, filter))
            .ToList();

        if (targets.Count == 0)
            return new CommandResult(CommandOutcome.NotFound, $"No actuators match '{filter}'.");

        bool? forced = request.Action switch
        {
            CommandAction.TurnOn => true,
            CommandAction.TurnOff => false,
            _ => null
        };

        //Only devices that actually need to change take part (and are remembered for Undo)
        var changing = targets.Where(v => forced is null || v.DesiredOn != forced).ToList();
        if (changing.Count == 0)
            return new CommandResult(CommandOutcome.NoChange, "All matching devices are already in that state.");

        var previous = changing.ToDictionary(v => v.Id, v => v.DesiredOn);
        bool target = forced ?? !changing[0].DesiredOn;

        string label = string.IsNullOrEmpty(filter) ? "actuators" : filter;
        string description = request.Action == CommandAction.TurnOff
            ? $"Shut down all '{label}' devices ({changing.Count})"
            : $"{(target ? "Turn ON" : "Toggle")} all '{label}' devices ({changing.Count})";

        return Apply(description, forced, changing.Select(v => v.Id).ToList(), previous, request.Action, request.Source);
    }

    private CommandResult Apply(string description, bool? forcedTarget, List<string> ids,
        Dictionary<string, bool> previous, CommandAction action, string source)
    {
        foreach (var id in ids)
        {
            bool newState = forcedTarget ?? !previous[id];
            SetDesired(id, newState);
            _engine.RecordCommand(id, newState);
        }

        var record = new CommandRecord
        {
            Id = Interlocked.Increment(ref _nextId),
            Kind = "Command",
            Description = description,
            Action = action,
            DeviceIds = ids,
            Source = string.IsNullOrWhiteSpace(source) ? "Manual" : source,
            TimestampUtc = DateTime.UtcNow
        };

        lock (_gate)
        {
            _undoStack.Push(new ExecutedCommand { Record = record, PreviousStates = previous });
            AddToHistory(record);
        }

        return new CommandResult(CommandOutcome.Ok, description, record.Clone());
    }

    private static bool ResolveTarget(CommandAction action, bool current) => action switch
    {
        CommandAction.TurnOn => true,
        CommandAction.TurnOff => false,
        _ => !current
    };

    private void SetDesired(string deviceId, bool on)
    {
        _registry.Update(deviceId, e => e.DesiredOn = on);
        //The actuator "acknowledges" by reporting its new state through the normal ingestion pipeline
        _pipeline.EnqueueActuator(deviceId, on);
    }

    /// <summary>Pops the most recent command and puts every affected device back as it was.</summary>
    public CommandResult Undo()
    {
        ExecutedCommand? last;
        lock (_gate)
        {
            if (!_undoStack.TryPop(out last))
                return new CommandResult(CommandOutcome.NoChange, "Nothing to undo.");

            last.Record.Undone = true;
        }

        foreach (var (deviceId, wasOn) in last.PreviousStates)
        {
            SetDesired(deviceId, wasOn);
        }

        var undoRecord = new CommandRecord
        {
            Id = Interlocked.Increment(ref _nextId),
            Kind = "Undo",
            Description = $"Undid: {last.Record.Description}",
            Action = last.Record.Action,
            DeviceIds = new List<string>(last.Record.DeviceIds),
            Source = "Undo",
            TimestampUtc = DateTime.UtcNow
        };

        lock (_gate)
        {
            AddToHistory(undoRecord);
        }

        return new CommandResult(CommandOutcome.Ok, undoRecord.Description, undoRecord.Clone());
    }

    private void AddToHistory(CommandRecord record)
    {
        _history.Add(record);
        if (_history.Count > 200) _history.RemoveAt(0);
    }

    /// <summary>Newest first.</summary>
    public List<CommandRecord> History(int take = 50)
    {
        lock (_gate)
        {
            return _history.AsEnumerable().Reverse().Take(take).Select(r => r.Clone()).ToList();
        }
    }

    /// <summary>What each actuator should currently be doing (devices poll this like a command channel).</summary>
    public Dictionary<string, bool> DesiredStates() =>
        _registry.Views()
            .Where(v => v.Category == SensorCategory.Actuator)
            .ToDictionary(v => v.Id, v => v.DesiredOn);
}
