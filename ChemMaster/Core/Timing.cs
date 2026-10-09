using System.Text.Json;

namespace SS14LocalMods.ChemMaster;

public sealed record ExecutionTiming(double ClicksPerSecond = 8, double SwitchPauseSeconds = 0.6, double ChaosPercent = 40)
{
    public bool Valid => double.IsFinite(ClicksPerSecond) && ClicksPerSecond is >= 1 and <= 20 &&
        double.IsFinite(SwitchPauseSeconds) && SwitchPauseSeconds is >= 0 and <= 3 &&
        double.IsFinite(ChaosPercent) && ChaosPercent is >= 0 and <= 100;

    // Chance of varying this interval; when chosen, shorten/lengthen it by up to 60%.
    // Sample once per command, never once per rendered frame.
    public TimeSpan Delay(bool reagentChanged, Func<double> random)
    {
        if (!Valid) throw new ArgumentException(Text.T("Неверные настройки скорости.", "Invalid timing settings."));
        var seconds = 1 / ClicksPerSecond;
        if (ChaosPercent > 0 && random() < ChaosPercent / 100) seconds *= 0.4 + 1.2 * random();
        if (reagentChanged) seconds += SwitchPauseSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}

public sealed class TimingStore(string path)
{
    private sealed record Document(int Version, ExecutionTiming Timing);
    public ExecutionTiming Load()
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException(Text.T("Файл настроек слишком большой.", "Settings file is too large."));
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
        if (document is not { Version: 1, Timing.Valid: true })
            throw new InvalidDataException(Text.T("Неверный файл настроек АВТО.", "Invalid AUTO settings file."));
        return document.Timing;
    }
    public void Save(ExecutionTiming timing)
    {
        if (!timing.Valid) throw new ArgumentException(Text.T("Неверные настройки скорости.", "Invalid timing settings."));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document(1, timing), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
