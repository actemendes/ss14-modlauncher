using System.Numerics;
using static SS14LocalMods.CrewConsole.ModText;

namespace SS14LocalMods.CrewConsole;

// Immutable observations: missing vitals/coordinates are never filled from a later sample.
public sealed record Observation(string Owner, string Sensor, string Name, string Job,
    TimeSpan Time, bool Alive, int? Damage, int? Threshold, string? Grid, Vector2? Position,
    int Segment = 0)
{
    public double? Ratio => Damage is >= 0 && Threshold is > 0 ? (double)Damage.Value / Threshold.Value : null;
    public int Severity => !Alive ? 6 : Ratio is not { } ratio ? -1 : Math.Clamp((int)Math.Round(4 * ratio), 0, 5);
    public string Health => Severity switch
    {
        6 => T("ПОГИБ", "DEAD"), 5 => T("КРИТИЧЕСКОЕ", "CRITICAL"), 4 => T("ТЯЖЁЛОЕ", "SEVERE"), 3 => T("ПЛОХОЕ", "POOR"),
        2 => T("СРЕДНЕЕ", "MODERATE"), 1 => T("ЛЁГКИЕ ТРАВМЫ", "MINOR INJURIES"), 0 => T("НОРМА", "HEALTHY"), _ => T("ЖИВ · НЕТ ПОКАЗАТЕЛЕЙ", "ALIVE · NO VITALS")
    };
    public string Color => Severity switch { >= 5 => "#ef6974", 4 => "#ee955f", 3 => "#e7bd59", 2 => "#64b5d6", -1 => "#8d9ba9", _ => "#58d6a4" };
    public string Clock => $"{(int)Time.TotalHours:00}:{Time.Minutes:00}:{Time.Seconds:00}";
    public string Location => Position is { } p ? $"X {p.X:0.0}   Y {p.Y:0.0}" : T("Координаты не переданы", "Coordinates unavailable");
}

public sealed class CrewHistory
{
    public const int MaxSamples = 3600;
    public const int MaxPeople = 512;
    public Dictionary<string, List<Observation>> People { get; } = new();
    public HashSet<string> Present { get; private set; } = new();
    private readonly Dictionary<string, int> _segments = new();
    public TimeSpan? LatestTime { get; private set; }
    public int Revision { get; private set; }

    public void Accept(IEnumerable<Observation> observations)
    {
        var samples = observations.GroupBy(x => x.Owner).Select(g => g
            .OrderByDescending(x => x.Position.HasValue).ThenByDescending(x => x.Ratio.HasValue)
            .ThenByDescending(x => x.Time).First()).ToArray();
        // An empty batch means unavailable telemetry, never proof of a death/disappearance.
        var next = samples.Select(x => x.Owner).ToHashSet();
        foreach (var missing in Present.Except(next))
            _segments[missing] = _segments.GetValueOrDefault(missing) + 1;
        foreach (var sample in samples)
        {
            if (!People.TryGetValue(sample.Owner, out var history))
            {
                if (People.Count >= MaxPeople)
                {
                    var oldest = People.Where(x => !next.Contains(x.Key)).MinBy(x => x.Value[^1].Time);
                    if (oldest.Key == null) continue;
                    People.Remove(oldest.Key); _segments.Remove(oldest.Key);
                }
                People[sample.Owner] = history = new();
            }
            var previous = history.LastOrDefault();
            if (previous != null && sample.Time <= previous.Time) continue;
            var segment = _segments.GetValueOrDefault(sample.Owner);
            if (previous != null && (previous.Sensor != sample.Sensor || previous.Grid != sample.Grid ||
                previous.Position.HasValue != sample.Position.HasValue || sample.Time - previous.Time > TimeSpan.FromSeconds(30)))
                segment++;
            _segments[sample.Owner] = segment;
            history.Add(sample with { Segment = segment });
            if (history.Count > MaxSamples) history.RemoveRange(0, history.Count - MaxSamples);
            if (LatestTime == null || sample.Time > LatestTime) LatestTime = sample.Time;
        }
        Present = next;
        Revision++;
    }

    public static Observation? At(IReadOnlyList<Observation> history, TimeSpan? cursor) =>
        history.LastOrDefault(x => cursor == null || x.Time <= cursor);

    public static bool Connect(Observation a, Observation b) => a.Owner == b.Owner &&
        a.Segment == b.Segment && a.Grid == b.Grid && a.Position != null && b.Position != null &&
        b.Time > a.Time && b.Time - a.Time <= TimeSpan.FromSeconds(30);
}
