//ST10440432
//Matteo Nusca
namespace SmartX.Shared.Models;

/// <summary>Result of a time-range query against the AVL history archive.</summary>
public sealed class ArchiveRangeResponse
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }

    /// <summary>Oldest to newest. If more readings matched than were asked for, these are the newest ones.</summary>
    public List<TimelinePoint> Points { get; set; } = new();

    public int TotalMatches { get; set; }
    public int Returned { get; set; }
    public bool Truncated { get; set; }

    /// <summary>How many tree nodes the search touched, against <see cref="ArchiveSize"/> readings stored.</summary>
    public int NodesVisited { get; set; }
    public int ArchiveSize { get; set; }
    public int TreeHeight { get; set; }
    public double ElapsedMicroseconds { get; set; }

    /// <summary>The span of history the archive holds, so a UI can offer sensible ranges.</summary>
    public DateTime? OldestUtc { get; set; }
    public DateTime? NewestUtc { get; set; }
}

/// <summary>Tree range query timed against a full scan of the same readings.</summary>
public sealed class ArchiveBenchmarkResponse
{
    public string DeviceId { get; set; } = string.Empty;
    public int ArchiveSize { get; set; }
    public int TreeHeight { get; set; }
    public int Matches { get; set; }
    public int NodesVisited { get; set; }
    public int Repetitions { get; set; }

    /// <summary>Average time for one query, in microseconds.</summary>
    public double TreeMicroseconds { get; set; }
    public double LinearMicroseconds { get; set; }

    /// <summary>How many times faster the tree was (linear time divided by tree time).</summary>
    public double SpeedUp { get; set; }

    /// <summary>True if both methods returned the same number of readings (a sanity check on the comparison).</summary>
    public bool ResultsMatch { get; set; }
}
