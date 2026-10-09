namespace SS14LocalMods.CrewConsole;

public static class TimelineScale
{
    public static double Fraction(TimeSpan time, TimeSpan first, TimeSpan last) => last <= first ? 0 :
        Math.Clamp((time - first).TotalSeconds / (last - first).TotalSeconds, 0, 1);

    // Floor in time, not in sample index: gaps retain their actual width on the axis.
    public static Observation? Pick(IReadOnlyList<Observation> history, double fraction)
    {
        if (history.Count == 0) return null;
        var start = history[0].Time;
        var ticks = (long)Math.Round((history[^1].Time - start).Ticks * Math.Clamp(fraction, 0, 1));
        return CrewHistory.At(history, start + TimeSpan.FromTicks(ticks)) ?? history[0];
    }

    public static double Ceiling(IReadOnlyList<Observation> history) => Math.Max(100,
        Math.Ceiling(history.Max(x => (x.Ratio ?? 0) * 100) / 50) * 50);
}
