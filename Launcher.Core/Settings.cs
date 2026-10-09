using System.Text.Json;
using System.Text.Json.Serialization;

namespace SS14ModLauncher.Core;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private string? _loadedPath;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SS14ModLauncher", "settings.json");
    public int Version { get; set; } = 1;
    public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    public string LauncherPath { get; set; } = "";
    public string UpdateRepository { get; set; } = "actemendes/ss14-modlauncher";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public string ActiveProfile { get; set; } = "Default";
    public Dictionary<string, List<string>> Profiles { get; set; } = new() { ["Default"] = ["crew-console"] };
    public Dictionary<string, Dictionary<string, string>> InstalledVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SetupDismissedWithoutRoot { get; set; }
    public Dictionary<string, string> SetupByRoot { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public string? LoadError { get; private set; }
    [JsonIgnore] public bool IsReadOnly => LoadError != null;
    [JsonIgnore] public IReadOnlyList<string> SelectedModIds => Profiles.TryGetValue(ActiveProfile, out var mods) ? mods.AsReadOnly() : Array.Empty<string>();

    public static AppSettings Load(string? path = null)
    {
        path = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(path)) return new AppSettings { _loadedPath = path };
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Settings exceed 1 MiB.");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Settings are empty.");
            settings.Validate();
            settings._loadedPath = path;
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // Preserve the user's file. Saving stays disabled until explicit recovery.
            return new AppSettings { _loadedPath = path, LoadError = e.Message };
        }
    }

    public void Save(string? path = null)
    {
        if (IsReadOnly) throw new InvalidOperationException("Settings could not be loaded and were preserved. Recover them before saving.");
        Validate();
        var destination = Path.GetFullPath(path ?? _loadedPath ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temporary, destination, overwrite: true);
            _loadedPath = destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Explicit recovery: preserve unreadable data before enabling new saves.</summary>
    public string? Recover()
    {
        if (!IsReadOnly) return null;
        var path = _loadedPath ?? DefaultPath;
        string? backup = null;
        if (File.Exists(path))
        {
            backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
            File.Copy(path, backup, overwrite: false);
        }
        LoadError = null;
        try { Save(path); }
        catch (Exception e) { LoadError = e.Message; throw; }
        return backup;
    }

    public void SetSelectedMods(IEnumerable<string> ids)
    {
        var selected = ids.Distinct(StringComparer.Ordinal).ToList();
        if (selected.Any(id => Catalog.ById(id) == null)) throw new ArgumentException("Unknown mod identifier.", nameof(ids));
        Profiles[ActiveProfile] = selected;
    }

    public void AddProfile(string name, IEnumerable<string>? ids = null)
    {
        name = name.Trim();
        if (!ValidProfileName(name)) throw new ArgumentException("Profile names must contain 1–40 visible characters.", nameof(name));
        if (Profiles.Keys.Any(key => key.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A profile with this name already exists.", nameof(name));
        if (Profiles.Count >= 32) throw new InvalidOperationException("At most 32 profiles are supported.");
        var selected = (ids ?? SelectedModIds).Distinct(StringComparer.Ordinal).ToList();
        if (selected.Any(id => Catalog.ById(id) == null)) throw new ArgumentException("Unknown mod identifier.", nameof(ids));
        Profiles.Add(name, selected);
        ActiveProfile = name;
    }

    public void DeleteProfile(string name)
    {
        if (Profiles.Count <= 1) throw new InvalidOperationException("Keep at least one profile.");
        if (!Profiles.Remove(name)) throw new ArgumentException("Unknown profile.", nameof(name));
        if (ActiveProfile == name) ActiveProfile = Profiles.Keys.First();
    }

    public IReadOnlyDictionary<string, string> VersionsFor(string launcherPath)
    {
        var root = Path.GetFullPath(launcherPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var entry = InstalledVersions.FirstOrDefault(pair => pair.Key.Equals(root, StringComparison.OrdinalIgnoreCase));
        return entry.Value ?? new Dictionary<string, string>();
    }

    public void RecordInstalledVersions(string launcherPath, IEnumerable<ModUpdate> updates)
    {
        var root = Path.GetFullPath(launcherPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var versions = VersionsFor(root).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var update in updates)
        {
            if (Catalog.ById(update.Id) == null || !SemanticVersion.TryParse(update.Version, out _)) throw new ArgumentException("Invalid installed mod version.", nameof(updates));
            versions[update.Id] = update.Version;
        }
        var existing = InstalledVersions.Keys.FirstOrDefault(key => key.Equals(root, StringComparison.OrdinalIgnoreCase));
        if (existing != null) InstalledVersions.Remove(existing);
        InstalledVersions[root] = versions;
    }

    public string? SetupStateFor(string? launcherPath)
    {
        var root = Setup.ResolveRoot(launcherPath);
        if (root.Length == 0) return null;
        return SetupByRoot.FirstOrDefault(pair => Setup.NormalizePath(pair.Key).Equals(root, StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>Record the user's choice in memory; the caller persists it with Save.</summary>
    public void SuppressSetupFor(string? launcherPath)
    {
        if (IsReadOnly) throw new InvalidOperationException("Recover damaged settings before saving a setup decision.");
        if (string.IsNullOrWhiteSpace(launcherPath)) { SetupDismissedWithoutRoot = true; return; }
        RecordSetupState(launcherPath, "dismissed");
    }

    /// <summary>Record completion only after setup succeeds; the caller persists it with Save.</summary>
    public void CompleteSetupFor(string launcherPath) => RecordSetupState(launcherPath, "completed");

    private void RecordSetupState(string launcherPath, string state)
    {
        if (IsReadOnly) throw new InvalidOperationException("Recover damaged settings before saving a setup decision.");
        var root = Setup.ResolveRoot(launcherPath);
        if (root.Length == 0) throw new ArgumentException("Choose an installation before recording setup completion.", nameof(launcherPath));
        var existing = SetupByRoot.Keys.FirstOrDefault(key => Setup.NormalizePath(key).Equals(root, StringComparison.OrdinalIgnoreCase));
        if (existing is null && SetupByRoot.Count >= 64) throw new InvalidOperationException("At most 64 installation setup decisions can be stored.");
        if (existing is not null) SetupByRoot.Remove(existing);
        SetupByRoot[root] = state;
    }

    private static bool ValidProfileName(string? name) => name is { Length: > 0 and <= 40 } && !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl);

    private void Validate()
    {
        if (Version != 1 || Language is not ("ru" or "en") || LauncherPath == null || LauncherPath.Length > 4096
            || UpdateRepository == null || UpdateRepository.Length > 140
            || (UpdateRepository.Length > 0 && !UpdateService.IsValidRepository(UpdateRepository))
            || Profiles == null || Profiles.Count is < 1 or > 32 || ActiveProfile == null || !Profiles.ContainsKey(ActiveProfile)
            || Profiles.Keys.Any(name => !ValidProfileName(name))
            || Profiles.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Profiles.Count
            || Profiles.Values.Any(mods => mods == null || mods.Count > Catalog.Bundled.Count
                || mods.Any(id => id == null || Catalog.ById(id) == null) || mods.Distinct().Count() != mods.Count)
            || InstalledVersions == null || InstalledVersions.Count > 64
            || InstalledVersions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 4096 || pair.Value == null
                || pair.Value.Count > Catalog.Bundled.Count || pair.Value.Any(mod => Catalog.ById(mod.Key) == null || !SemanticVersion.TryParse(mod.Value, out _)))
            || SetupByRoot == null || SetupByRoot.Count > 64
            || SetupByRoot.Any(pair => !Setup.IsValidStoredRoot(pair.Key) || pair.Value is not ("dismissed" or "completed"))
            || SetupByRoot.Keys.Select(Setup.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != SetupByRoot.Count)
            throw new InvalidDataException("Settings have an unsupported version or invalid values.");
    }
}
