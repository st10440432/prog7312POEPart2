//ST10440432
//Matteo Nusca
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Predictive action and recommendation engine.
///
/// It learns "what usually happens next" from the operators' own behaviour. Every alert, search and manual
/// command becomes a token (e.g. "Alert:AA:..:02", "Search:AA:..:06", "Cmd:AA:..:06:On"). For each new event
/// the engine looks back over a sliding 5 minute window (Queue&lt;T&gt;) and counts, for every earlier event P,
/// that event E followed it:
///
///     transitions[P][E]++            (Dictionary&lt;string, Dictionary&lt;string,int&gt;&gt;)
///     confidence(P -&gt; E) = transitions[P][E] / resolved[P]
///
/// When an alert is live (or the operator just did something), every learned follow-up with enough support and
/// confidence becomes a candidate. Candidates are ranked in a PriorityQueue by confidence x support x learned
/// feedback weight, and surfaced as "Suggested Actions". Separately, the engine scans live telemetry for devices
/// that are heading towards trouble and surfaces them as "Automated Insights".
/// </summary>
public sealed class InsightEngine
{
    private sealed class WindowEvent
    {
        public required string Token { get; init; }
        public required DateTime Ts { get; init; }
        public bool Resolved { get; set; }
        public HashSet<string> Followers { get; } = new();
    }

    public const int MinSupport = 2;
    public const double MinConfidence = 0.4;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly Queue<WindowEvent> _window = new();
    private readonly Dictionary<string, Dictionary<string, int>> _transitions = new();
    private readonly Dictionary<string, int> _resolved = new();
    private readonly Dictionary<string, double> _weights = new();
    private readonly Dictionary<string, DateTime> _dismissedUntil = new();
    private readonly object _gate = new();
    private int _eventsLearned;

    private readonly DeviceRegistry _registry;
    private readonly AlertTracker _alerts;
    private readonly TelemetryHistory _history;

    public InsightEngine(DeviceRegistry registry, AlertTracker alerts, TelemetryHistory history)
    {
        _registry = registry;
        _alerts = alerts;
        _history = history;
    }

    // ---------------------------------------------------------------- learning

    public void RecordAlert(string deviceId, DateTime? at = null) => Record($"Alert:{deviceId}", at ?? DateTime.UtcNow);
    public void RecordDisconnect(string deviceId, DateTime? at = null) => Record($"Disconnect:{deviceId}", at ?? DateTime.UtcNow);
    public void RecordSearch(string deviceId, DateTime? at = null) => Record($"Search:{deviceId}", at ?? DateTime.UtcNow);
    public void RecordCommand(string deviceId, bool on, DateTime? at = null) =>
        Record($"Cmd:{deviceId}:{(on ? "On" : "Off")}", at ?? DateTime.UtcNow);

    private void Record(string token, DateTime ts)
    {
        lock (_gate)
        {
            //Expire events that have left the window (an expired occurrence counts as "resolved")
            while (_window.TryPeek(out var oldest) && ts - oldest.Ts > Window)
            {
                _window.Dequeue();
                MarkResolved(oldest);
            }

            //This event is a follower of every earlier event still inside the window
            foreach (var earlier in _window)
            {
                if (earlier.Token == token) continue;
                if (!earlier.Followers.Add(token)) continue; //count a follower once per occurrence

                MarkResolved(earlier);

                if (!_transitions.TryGetValue(earlier.Token, out var followers))
                {
                    followers = new Dictionary<string, int>();
                    _transitions[earlier.Token] = followers;
                }

                followers[token] = followers.GetValueOrDefault(token) + 1;
            }

            _window.Enqueue(new WindowEvent { Token = token, Ts = ts });
            _eventsLearned++;
        }
    }

    private void MarkResolved(WindowEvent e)
    {
        if (e.Resolved) return;
        e.Resolved = true;
        _resolved[e.Token] = _resolved.GetValueOrDefault(e.Token) + 1;
    }

    public void Feedback(string suggestionId, bool accepted)
    {
        lock (_gate)
        {
            double weight = _weights.GetValueOrDefault(suggestionId, 1.0);
            if (accepted)
            {
                _weights[suggestionId] = Math.Min(2.0, weight * 1.25);
                _dismissedUntil.Remove(suggestionId);
            }
            else
            {
                _weights[suggestionId] = Math.Max(0.1, weight * 0.6);
                _dismissedUntil[suggestionId] = DateTime.UtcNow.AddSeconds(90);
            }
        }
    }

    // ------------------------------------------------------------- recommending

    public InsightsResponse Build()
    {
        var suggestions = BuildSuggestions(5);
        var insights = BuildInsights();

        //Devices the operator is likely to inspect next are highlighted as informational insights
        foreach (var s in suggestions.Where(s => s.Kind == "Inspect"))
        {
            insights.Add(new AutomatedInsight
            {
                DeviceId = s.DeviceId,
                DeviceName = s.DeviceName,
                Severity = InsightSeverity.Info,
                Kind = "Predicted",
                Message = $"Operators usually check this next ({s.Confidence * 100:0}% of the time)."
            });
        }

        int patterns;
        int events;
        lock (_gate)
        {
            patterns = _transitions.Values.Sum(d => d.Count);
            events = _eventsLearned;
        }

        return new InsightsResponse
        {
            Suggestions = suggestions,
            Insights = insights.OrderByDescending(i => i.Severity).ToList(),
            EventsLearned = events,
            PatternsKnown = patterns
        };
    }

    private sealed record RawCandidate(string Trigger, string Effect, int Count, int Resolved, bool TriggerIsAlert, double Weight, DateTime? DismissedUntil);

    private List<SuggestedAction> BuildSuggestions(int top)
    {
        var now = DateTime.UtcNow;
        //A disconnect is a different kind of event from an out-of-range / spike alert, so it has its own token
        var activeAlertTokens = _alerts.Snapshot()
            .Select(a => a.Type == AlertTracker.Disconnected ? $"Disconnect:{a.DeviceId}" : $"Alert:{a.DeviceId}")
            .Distinct().ToList();

        List<string> recentTokens;
        var raw = new List<RawCandidate>();

        lock (_gate)
        {
            recentTokens = _window.Where(w => now - w.Ts <= Window).Select(w => w.Token).Distinct().ToList();

            //Triggers = live alerts + what the operator has just done
            var triggers = activeAlertTokens
                .Select(t => (Token: t, IsAlert: true))
                .Concat(recentTokens.Where(t => !t.StartsWith("Alert:") && !t.StartsWith("Disconnect:")).Select(t => (Token: t, IsAlert: false)))
                .DistinctBy(t => t.Token);

            foreach (var (trigger, isAlert) in triggers)
            {
                if (!_transitions.TryGetValue(trigger, out var followers)) continue;
                int resolved = _resolved.GetValueOrDefault(trigger);

                foreach (var (effect, count) in followers)
                {
                    raw.Add(new RawCandidate(trigger, effect, count, resolved, isAlert,
                        _weights.GetValueOrDefault(effect, 1.0),
                        _dismissedUntil.TryGetValue(effect, out var until) ? until : null));
                }
            }
        }

        var best = new Dictionary<string, SuggestedAction>();

        foreach (var c in raw)
        {
            if (c.Effect == c.Trigger || c.Count < MinSupport) continue;
            if (c.DismissedUntil is not null && c.DismissedUntil > now) continue;

            double confidence = (double)c.Count / Math.Max(c.Resolved, c.Count);
            if (confidence < MinConfidence) continue;

            var parts = c.Effect.Split(':', 2);
            if (parts.Length < 2) continue;
            string kind = parts[0];

            SuggestedAction? suggestion = null;

            if (kind == "Cmd")
            {
                //Cmd:{deviceId}:{On|Off}  (device ids contain ':' so split from the right)
                int split = parts[1].LastIndexOf(':');
                if (split < 0) continue;
                string deviceId = parts[1][..split];
                bool turnOn = parts[1][(split + 1)..] == "On";

                var device = _registry.View(deviceId);
                if (device is null || device.Category != SensorCategory.Actuator) continue;
                if (device.DesiredOn == turnOn) continue; //already in that state: nothing to suggest

                suggestion = new SuggestedAction
                {
                    Id = c.Effect,
                    Kind = "Command",
                    Title = $"{(turnOn ? "Turn ON" : "Turn OFF")} {device.Name}",
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    Action = turnOn ? CommandAction.TurnOn : CommandAction.TurnOff
                };
            }
            else if (kind == "Search")
            {
                if (recentTokens.Contains(c.Effect)) continue; //operator already looked at it
                var device = _registry.View(parts[1]);
                if (device is null) continue;

                suggestion = new SuggestedAction
                {
                    Id = c.Effect,
                    Kind = "Inspect",
                    Title = $"Inspect {device.Name}",
                    DeviceId = device.Id,
                    DeviceName = device.Name
                };
            }

            if (suggestion is null) continue;

            suggestion.Confidence = Math.Round(confidence, 2);
            suggestion.Support = c.Count;
            suggestion.Reason = $"After {Describe(c.Trigger)}, this followed {c.Count} of {Math.Max(c.Resolved, c.Count)} times ({confidence * 100:0}%).";
            suggestion.Score = confidence * (1 + Math.Log(c.Count)) * c.Weight * (c.TriggerIsAlert ? 1.5 : 1.0);

            if (!best.TryGetValue(c.Effect, out var existing) || suggestion.Score > existing.Score)
                best[c.Effect] = suggestion;
        }

        //Rank with a PriorityQueue (highest score first)
        var ranking = new PriorityQueue<SuggestedAction, double>();
        foreach (var s in best.Values) ranking.Enqueue(s, -s.Score);

        var result = new List<SuggestedAction>();
        while (result.Count < top && ranking.TryDequeue(out var next, out _))
        {
            next.Score = Math.Round(next.Score, 2);
            result.Add(next);
        }

        return result;
    }

    private string Describe(string token)
    {
        var parts = token.Split(':', 2);
        if (parts.Length < 2) return token;

        switch (parts[0])
        {
            case "Alert":
                return $"an alert on {NameOf(parts[1])}";
            case "Disconnect":
                return $"{NameOf(parts[1])} going offline";
            case "Search":
                return $"you viewed {NameOf(parts[1])}";
            case "Cmd":
                int split = parts[1].LastIndexOf(':');
                if (split < 0) return token;
                return $"you turned {(parts[1][(split + 1)..] == "On" ? "ON" : "OFF")} {NameOf(parts[1][..split])}";
            default:
                return token;
        }
    }

    private string NameOf(string deviceId) => _registry.View(deviceId)?.Name ?? deviceId;

    // --------------------------------------------------------- proactive insights

    private List<AutomatedInsight> BuildInsights()
    {
        var list = new List<AutomatedInsight>();
        var alerts = _alerts.Snapshot();
        var alertedDevices = alerts.Select(a => a.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var a in alerts)
        {
            list.Add(new AutomatedInsight
            {
                DeviceId = a.DeviceId,
                DeviceName = a.DeviceName,
                Severity = a.Type == AlertTracker.Disconnected ? InsightSeverity.Warning : InsightSeverity.Critical,
                Kind = a.Type,
                Message = a.Message
            });
        }

        foreach (var device in _registry.Views())
        {
            if (alertedDevices.Contains(device.Id)) continue;

            switch (device.Category)
            {
                case SensorCategory.Environmental:
                {
                    var values = _history.Tail(device.Id, 8).Select(p => p.Value).ToArray();
                    if (values.Length < 5) break;
                    double slope = Slope(values);
                    double last = values[^1];

                    if (slope > 0.5 && last < AnomalyRules.EnvironmentalHigh && last + slope * 6 >= AnomalyRules.EnvironmentalHigh)
                    {
                        int steps = (int)Math.Ceiling((AnomalyRules.EnvironmentalHigh - last) / slope);
                        list.Add(Trend(device, $"Rising +{slope:0.0} per reading; projected to pass {AnomalyRules.EnvironmentalHigh:0} in about {steps} readings."));
                    }
                    else if (slope < -0.5 && last > AnomalyRules.EnvironmentalLow && last + slope * 6 <= AnomalyRules.EnvironmentalLow)
                    {
                        int steps = (int)Math.Ceiling((last - AnomalyRules.EnvironmentalLow) / -slope);
                        list.Add(Trend(device, $"Falling {slope:0.0} per reading; projected to drop below {AnomalyRules.EnvironmentalLow:0} in about {steps} readings."));
                    }
                    break;
                }
                case SensorCategory.PowerConsumption:
                {
                    var values = _history.Tail(device.Id, 6).Select(p => p.Value).ToArray();
                    if (values.Length < 4) break;
                    double slope = Slope(values);
                    if (slope > AnomalyRules.PowerSpikeWatts / 6.0)
                        list.Add(Trend(device, $"Load ramping up +{slope:0} W per reading; a spike is likely soon."));
                    break;
                }
                case SensorCategory.Actuator:
                {
                    var states = _history.Tail(device.Id, 10).Select(p => p.Value >= 0.5).ToArray();
                    int flips = 0;
                    for (int i = 1; i < states.Length; i++) if (states[i] != states[i - 1]) flips++;
                    if (flips >= 4)
                    {
                        list.Add(new AutomatedInsight
                        {
                            DeviceId = device.Id,
                            DeviceName = device.Name,
                            Severity = InsightSeverity.Warning,
                            Kind = "Flapping",
                            Message = $"Changed state {flips} times in its last {states.Length} reports (possible relay chatter)."
                        });
                    }
                    break;
                }
            }
        }

        return list;
    }

    private static AutomatedInsight Trend(DeviceView device, string message) => new()
    {
        DeviceId = device.Id,
        DeviceName = device.Name,
        Severity = InsightSeverity.Warning,
        Kind = "Trend",
        Message = message
    };

    /// <summary>Least-squares slope of the values against their index.</summary>
    private static double Slope(double[] values)
    {
        int n = values.Length;
        double meanX = (n - 1) / 2.0;
        double meanY = values.Average();
        double num = 0, den = 0;
        for (int i = 0; i < n; i++)
        {
            num += (i - meanX) * (values[i] - meanY);
            den += (i - meanX) * (i - meanX);
        }
        return den == 0 ? 0 : num / den;
    }
}
