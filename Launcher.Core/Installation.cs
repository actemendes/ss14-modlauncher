using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SS14ModLauncher.Core;

public sealed record InstallationStatus(string State, string Detail);
public sealed record SelectedMod(string File, string Sha256);
public sealed record ModSelection(int Version, string Language, IReadOnlyList<SelectedMod> EnabledMods);

/// <summary>Stable codes let the desktop application translate actionable failures.</summary>
public sealed class InstallationException(string code, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>
/// Owns local installation state. Every mutation is serialized and journalled; no Steam
/// configuration or downloaded game assembly is touched beyond the explicitly selected root.
/// </summary>
public static class Installation
{
    private const string Loader = "loader/SS14.Loader.dll";
    private const string Home = "loader/SS14LocalMods";
    private const string Record = Home + "/installation.json";
    private const string Backup = Home + "/SS14.Loader.original.dll";
    private const string Selection = Home + "/selection.json";
    private const string Ownership = Home + "/ownership.json";
    private const string Launcher = "SS14.Launcher.exe";
    private const string CleanLauncher = "SS14.Launcher.clean.exe";
    private const string StableLauncher = "SS14ModLauncher/SS14ModLauncher.exe";
    private const string SteamRecord = "SS14ModLauncher/steam.json";
    private const string StableRecord = "SS14ModLauncher/launcher.json";
    private const string Preferences = "SS14ModLauncher/preferences.json";
    private const string JournalDirectory = ".ss14-modlauncher-transaction";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string[] RuntimeFiles = ["SS14LocalMods.Bootstrap.dll", "0Harmony.dll"];
    private static readonly string[] LegacyMods = ["HelloWorld.Mod.dll", "CrewConsole.Mod.dll"];

    // The first two properties deliberately match the original installer state format.
    private sealed record LoaderState(string OriginalHash, string PatchedHash, Dictionary<string, string>? OwnedFiles = null, int Version = 2);
    private sealed record SteamState(string OriginalHash, string WrapperHash, int Version = 1);
    private sealed record StableState(string Hash, int Version = 1);
    private sealed record IntegrationPreferences(bool? SteamIntegration, int Version = 1);
    private sealed record JournalEntry(string Path, string? BeforeHash, string? AfterHash, string? Snapshot);
    private sealed record Journal(int Version, IReadOnlyList<JournalEntry> Entries);

    public static InstallationStatus Inspect(string root)
    {
        try
        {
            root = ValidateRoot(root);
            if (Directory.Exists(At(root, JournalDirectory)) && !File.Exists(At(root, JournalDirectory + "/committed")))
                return new("recovery", "interrupted-transaction");
            ValidateSteamForRestore(root);
            var steam = ReadSteamState(root);
            if (steam is not null && Same(HashFile(At(root, Launcher)), steam.WrapperHash)
                && (!File.Exists(At(root, StableLauncher)) || !Same(HashFile(At(root, StableLauncher)), steam.WrapperHash)))
                return new("recovery", "steam-wrapper-missing-or-changed");
            var state = ReadLoaderState(root);
            if (state is null)
            {
                if (File.Exists(At(root, Backup))) return new("recovery", "orphan-backup");
                if (ContainsPatch(At(root, Loader))) return new("recovery", "untracked-patch");
                return new("clean", "ready-to-install");
            }
            ValidateBackup(root, state);
            var current = HashFile(At(root, Loader));
            if (Same(current, state.OriginalHash)) return new("clean", "original-restored-by-platform");
            if (!Same(current, state.PatchedHash)) return new("changed", "loader-changed");
            if (state.OwnedFiles is null) return new("recovery", "legacy-installation");
            if (state.OwnedFiles is not null)
            {
                foreach (var (file, hash) in state.OwnedFiles)
                    if (!File.Exists(At(root, PayloadPath(file))) || !Same(HashFile(At(root, PayloadPath(file))), hash))
                        return new("recovery", "payload-changed-or-missing");
            }
            return new("installed", "ready-to-launch");
        }
        catch (InstallationException ex) { return new(ex.Code is "loader-changed" or "steam-changed" ? "changed" : "recovery", ex.Code); }
        catch (Exception) { return new("recovery", "cannot-read-installation"); }
    }

    public static void Install(string root, IReadOnlyDictionary<string, byte[]> payload, IReadOnlyList<string> enabledFiles, string language)
    {
        root = ValidateRoot(root);
        using var guard = Lock(root);
        EnsureNotRunning(root);
        RecoverPending(root);
        Commit(root, PrepareInstallWrites(root, payload, enabledFiles, language));
    }

    /// <summary>
    /// Installs mods and, for a detected Steam installation, its default launcher bridge
    /// in one transaction. A prior explicit integration preference is respected.
    /// </summary>
    public static void InstallWithDefaults(string root, IReadOnlyDictionary<string, byte[]> payload,
        IReadOnlyList<string> enabledFiles, string language, string modLauncherExecutable)
    {
        root = ValidateRoot(root);
        using var guard = Lock(root);
        EnsureNotRunning(root);
        RecoverPending(root);
        var writes = PrepareInstallWrites(root, payload, enabledFiles, language);
        if (ShouldEnableSteamByDefault(root))
            foreach (var (path, bytes) in PrepareSteamWrites(root, modLauncherExecutable)) writes.Add(path, bytes);
        Commit(root, writes);
    }

    private static Dictionary<string, byte[]?> PrepareInstallWrites(string root, IReadOnlyDictionary<string, byte[]> payload,
        IReadOnlyList<string> enabledFiles, string language)
    {
        ValidateLanguage(language);
        if (payload is null || RuntimeFiles.Any(x => !payload.ContainsKey(x)))
            throw Error("payload-invalid", "The bootstrap and Harmony payloads are required.");
        foreach (var (file, bytes) in payload)
        {
            _ = PayloadPath(file);
            if (bytes is null || bytes.Length == 0 || bytes.Length > 128 * 1024 * 1024)
                throw Error("payload-invalid", "A payload file is empty or too large.");
        }
        if (payload.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != payload.Count)
            throw Error("payload-invalid", "Payload filenames must be unique, including letter case.");
        ValidateSteamForRestore(root);
        var state = ReadLoaderState(root);
        byte[] original;
        if (state is not null)
        {
            ValidateBackup(root, state);
            var current = HashFile(At(root, Loader));
            if (!Same(current, state.PatchedHash) && !Same(current, state.OriginalHash))
                throw Error("loader-changed", "The loader changed after installation. Verify the game files before installing again.");
            original = File.ReadAllBytes(At(root, Backup));
        }
        else
        {
            if (File.Exists(At(root, Backup))) throw Error("orphan-backup", "An untracked loader backup exists and will not be overwritten.");
            original = File.ReadAllBytes(At(root, Loader));
        }
        // Rebuild from the verified original, never from an already patched assembly.
        var patched = Patch(original);
        var writes = new Dictionary<string, byte[]?>();
        if (state is null) writes[Backup] = original;
        var owned = state?.OwnedFiles is null ? ReadOwnership(root)
            : new Dictionary<string, string>(state.OwnedFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var (file, bytes) in payload)
        {
            var path = PayloadPath(file);
            if (File.Exists(At(root, path)))
            {
                var existingHash = HashFile(At(root, path));
                var legacyOwned = state is not null && state.OwnedFiles is null && (RuntimeFiles.Contains(file) || LegacyMods.Contains(file));
                if (owned.TryGetValue(file, out var oldHash) && !Same(existingHash, oldHash))
                    throw Error("payload-conflict", $"The installed file {file} was modified outside this launcher.");
                if (!owned.ContainsKey(file) && !legacyOwned && !Same(existingHash, Hash(bytes)))
                    throw Error("payload-conflict", $"An unrelated file named {file} already exists.");
            }
            writes[path] = bytes;
            owned[file] = Hash(bytes);
        }
        writes[Selection] = EncodeSelection(root, enabledFiles, language, payload);
        writes[Ownership] = Encode(owned);
        writes[Record] = Encode(new LoaderState(Hash(original), Hash(patched), owned));
        // Publish the loader last, after its dependencies, selection, backup and state.
        writes[Loader] = patched;
        return writes;
    }

    public static void SetSelection(string root, IReadOnlyList<string> enabledFiles, string language)
    {
        root = ValidateRoot(root);
        using var guard = Lock(root);
        EnsureNotRunning(root);
        RecoverPending(root);
        var state = ReadLoaderState(root) ?? throw Error("not-installed", "Install the mod launcher first.");
        if (state.OwnedFiles is null) throw Error("upgrade-required", "Update the prototype installation before selecting mods.");
        ValidateBackup(root, state);
        if (!Same(HashFile(At(root, Loader)), state.PatchedHash))
            throw Error("loader-changed", "The loader is no longer the installed version.");
        foreach (var (file, hash) in state.OwnedFiles ?? ReadOwnership(root))
            if (!File.Exists(At(root, PayloadPath(file))) || !Same(HashFile(At(root, PayloadPath(file))), hash))
                throw Error("payload-conflict", $"The installed file {file} is missing or was changed outside this launcher.");
        Commit(root, new Dictionary<string, byte[]?> { [Selection] = EncodeSelection(root, enabledFiles, language, null) });
    }

    public static ModSelection? ReadSelection(string root)
    {
        root = ValidateRoot(root);
        if (!File.Exists(At(root, Selection))) return null;
        var selection = Read<ModSelection>(At(root, Selection));
        if (selection.Version != 1 || selection.EnabledMods is null) throw Error("state-invalid", "The selection file is invalid.");
        ValidateLanguage(selection.Language);
        foreach (var mod in selection.EnabledMods)
            if (mod is null || !IsModFile(mod.File) || !IsHash(mod.Sha256)) throw Error("state-invalid", "The selection file is invalid.");
        if (selection.EnabledMods.Select(m => m.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selection.EnabledMods.Count)
            throw Error("state-invalid", "The selection contains duplicate mods.");
        return selection;
    }

    /// <summary>Return only recorded, hash-verified mod bytes, including after a clean restore.</summary>
    public static IReadOnlyDictionary<string, byte[]> ReadVerifiedInstalledMods(string root)
    {
        root = ValidateRoot(root);
        var state = ReadLoaderState(root);
        var owned = state?.OwnedFiles ?? ReadOwnership(root);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, expected) in owned)
        {
            if (!IsModFile(file)) continue;
            var path = At(root, PayloadPath(file));
            if (!File.Exists(path)) continue; // A missing file can be supplied by the bundled payload.
            var bytes = File.ReadAllBytes(path);
            if (!Same(Hash(bytes), expected)) throw Error("payload-conflict", $"The installed file {file} was modified outside this launcher.");
            result.Add(file, bytes);
        }
        return result;
    }

    public static void Restore(string root)
    {
        root = ValidateRoot(root);
        using var guard = Lock(root);
        EnsureNotRunning(root);
        RecoverPending(root);
        // Validate BOTH components before staging any write. Unknown platform updates are preserved.
        var writes = SteamRestoreWrites(root);
        var state = ReadLoaderState(root);
        if (state is not null)
        {
            ValidateBackup(root, state);
            var current = HashFile(At(root, Loader));
            if (!Same(current, state.OriginalHash) && !Same(current, state.PatchedHash))
                throw Error("loader-changed", "The loader changed after installation. No files have been restored.");
            writes[Loader] = File.ReadAllBytes(At(root, Backup));
            var owned = state.OwnedFiles ?? RuntimeFiles.Concat(LegacyMods).Where(f => File.Exists(At(root, PayloadPath(f))))
                .ToDictionary(f => f, f => HashFile(At(root, PayloadPath(f))), StringComparer.OrdinalIgnoreCase);
            writes[Ownership] = Encode(owned);
            writes[Record] = null;
            writes[Backup] = null;
        }
        else if (File.Exists(At(root, Backup)) || ContainsPatch(At(root, Loader)))
            throw Error("untracked-patch", "An untracked patch or backup cannot be safely restored automatically.");
        // Keep mod files and preferences, including third-party files. An original loader never loads them.
        Commit(root, writes);
    }

    public static string GetLaunchExecutable(string root)
    {
        root = ValidateRoot(root);
        if (Directory.Exists(At(root, JournalDirectory)) && !File.Exists(At(root, JournalDirectory + "/committed")))
            throw Error("interrupted-transaction", "Finish installation recovery before launching.");
        var state = ReadSteamState(root);
        if (state is null) return At(root, Launcher);
        ValidateSteamForRestore(root);
        return Same(HashFile(At(root, Launcher)), state.WrapperHash) ? At(root, CleanLauncher) : At(root, Launcher);
    }

    public static bool IsSteamEnabled(string root)
    {
        try
        {
            root = ValidateRoot(root);
            var state = ReadSteamState(root);
            if (state is null) return false;
            ValidateSteamForRestore(root);
            return Same(HashFile(At(root, Launcher)), state.WrapperHash) && File.Exists(At(root, StableLauncher))
                && Same(HashFile(At(root, StableLauncher)), state.WrapperHash);
        }
        catch { return false; }
    }

    /// <summary>Detect the selected SS14 folder from Steam's library layout and its matching app manifest.</summary>
    public static bool IsSteamInstallation(string root)
    {
        try
        {
            root = ValidateRoot(root);
            var bin = new DirectoryInfo(root);
            var game = bin.Parent;
            var common = game?.Parent;
            var steamapps = common?.Parent;
            if (!bin.Name.Equals("bin_x64", StringComparison.OrdinalIgnoreCase) || game is null
                || common?.Name.Equals("common", StringComparison.OrdinalIgnoreCase) != true
                || steamapps?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) != true) return false;
            foreach (var manifest in steamapps.EnumerateFiles("appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                if (manifest.Length is <= 0 or > 512 * 1024) continue;
                EnsureNoLinks(manifest.FullName);
                var text = File.ReadAllText(manifest.FullName);
                if (!Regex.IsMatch(text, @"\A\s*""AppState""\s*\{", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
                var id = ManifestValue(text, "appid");
                var installDir = ManifestValue(text, "installdir");
                if (id is null || !Regex.IsMatch(id, @"\A[0-9]+\z")
                    || !manifest.Name.Equals("appmanifest_" + id + ".acf", StringComparison.OrdinalIgnoreCase)
                    || installDir is null || installDir.IndexOfAny(['/', '\\', ':']) >= 0 || installDir is "." or "..") continue;
                if (game.Name.Equals(installDir, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InstallationException) { }
        return false;
    }

    /// <summary>Resolve the Steam default without modifying any files.</summary>
    public static bool ShouldEnableSteamByDefault(string root)
    {
        root = ValidateRoot(root);
        if (!IsSteamInstallation(root)) return false;
        var preference = ReadIntegrationPreferences(root);
        return preference?.SteamIntegration ?? true;
    }

    private static string? ManifestValue(string text, string key)
    {
        var matches = Regex.Matches(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"\\\\]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return matches.Count == 1 ? matches[0].Groups[1].Value : null;
    }

    public static void EnableSteam(string root, string modLauncherExecutable)
    {
        root = ValidateRoot(root);
        using var guard = Lock(root);
        EnsureNotRunning(root);
        RecoverPending(root);
        var installed = ReadLoaderState(root) ?? throw Error("not-installed", "Install the mod launcher before enabling Steam integration.");
        ValidateBackup(root, installed);
        if (!Same(HashFile(At(root, Loader)), installed.PatchedHash)) throw Error("loader-changed", "Install against the current game loader first.");
        Commit(root, PrepareSteamWrites(root, modLauncherExecutable));
    }

    private static Dictionary<string, byte[]?> PrepareSteamWrites(string root, string modLauncherExecutable)
    {
        if (!File.Exists(At(root, "SS14.Launcher.dll")))
            throw Error("steam-unsupported", "Steam integration requires SS14.Launcher.dll beside its executable.");
        var source = Path.GetFullPath(modLauncherExecutable);
        if (string.Equals(source, At(root, Launcher), StringComparison.OrdinalIgnoreCase) || string.Equals(source, At(root, CleanLauncher), StringComparison.OrdinalIgnoreCase))
            throw Error("steam-unsupported", "Use the published SS14ModLauncher executable as the integration payload.");
        var wrapper = File.ReadAllBytes(source);
        if (wrapper.Length < 64 || wrapper[0] != 'M' || wrapper[1] != 'Z')
            throw Error("steam-unsupported", "Steam integration requires a published Windows executable.");
        var existing = ReadSteamState(root);
        byte[] original;
        if (existing is null)
        {
            if (File.Exists(At(root, CleanLauncher))) throw Error("steam-backup-conflict", "An untracked clean launcher backup already exists.");
            original = File.ReadAllBytes(At(root, Launcher));
        }
        else
        {
            ValidateSteamForRestore(root);
            original = File.ReadAllBytes(At(root, CleanLauncher));
        }
        if (!LooksLikeOriginalLauncher(original))
            throw Error("steam-unsupported", "The original executable is not a supported SS14 launcher apphost.");
        if (Same(Hash(original), Hash(wrapper))) throw Error("steam-unsupported", "The wrapper cannot be used as the original launcher.");
        if (File.Exists(At(root, StableLauncher)) && !Same(HashFile(At(root, StableLauncher)), Hash(wrapper)))
        {
            var prior = ReadStableState(root);
            var currentHash = HashFile(At(root, StableLauncher));
            if ((existing is null || !Same(currentHash, existing.WrapperHash)) && (prior is null || !Same(currentHash, prior.Hash)))
                throw Error("steam-changed", "The stable launcher was changed outside this installation.");
        }
        var writes = new Dictionary<string, byte[]?>();
        if (existing is null) writes[CleanLauncher] = original;
        writes[StableLauncher] = wrapper;
        writes[StableRecord] = Encode(new StableState(Hash(wrapper)));
        writes[SteamRecord] = Encode(new SteamState(Hash(original), Hash(wrapper)));
        writes[Preferences] = Encode(new IntegrationPreferences(true));
        writes[Launcher] = wrapper;
        return writes;
    }

    public static void DisableSteam(string root)
    {
        root = ValidateRoot(root);
        using var guard = Lock(root);
        EnsureNotRunning(root);
        RecoverPending(root);
        var writes = SteamRestoreWrites(root);
        writes[Preferences] = Encode(new IntegrationPreferences(false));
        Commit(root, writes);
    }

    /// <summary>
    /// Explicit recovery after the user has verified game files in Steam. This is never
    /// called automatically: the caller must obtain confirmation that the current files
    /// are trusted originals. Prior backups and state are archived, not discarded.
    /// </summary>
    public static string RebaseAfterPlatformUpdate(string root)
    {
        root = ValidateRoot(root);
        using var guard = Lock(root);
        EnsureNotRunning(root);
        RecoverPending(root);
        if (ContainsPatch(At(root, Loader)))
            throw Error("verify-game-first", "Verify the game files in Steam first; the current loader still contains the mod patch.");
        var currentLoader = File.ReadAllBytes(At(root, Loader));
        _ = Patch(currentLoader); // Prove that the newly supplied original has a compatible entry point.
        var currentLauncher = File.ReadAllBytes(At(root, Launcher));
        var oldSteam = ReadSteamState(root);
        var stable = ReadStableState(root);
        var launcherHash = Hash(currentLauncher);
        if (oldSteam is not null && Same(launcherHash, oldSteam.WrapperHash) || stable is not null && Same(launcherHash, stable.Hash))
            throw Error("verify-game-first", "The Steam executable is still the mod launcher. Verify the game files before adopting new originals.");
        // Original .NET apphosts embed their target assembly name. This is a compatibility
        // check, not authentication; the caller's explicit Steam-verification confirmation is essential.
        if (!LooksLikeOriginalLauncher(currentLauncher) || !File.Exists(At(root, "SS14.Launcher.dll")))
            throw Error("verify-game-first", "The current executable does not look like the original SS14 launcher apphost.");
        var archive = Home + "/History/" + Guid.NewGuid().ToString("N");
        var writes = new Dictionary<string, byte[]?>();
        foreach (var source in new[] { Record, Backup, SteamRecord, CleanLauncher })
        {
            if (!File.Exists(At(root, source))) continue;
            writes[archive + "/" + Path.GetFileName(source)] = File.ReadAllBytes(At(root, source));
            writes[source] = null;
        }
        // Retain verified payload ownership so installation can update the previous files.
        var oldLoader = ReadLoaderState(root);
        if (oldLoader?.OwnedFiles is not null) writes[Ownership] = Encode(oldLoader.OwnedFiles);
        writes[archive + "/adoption.json"] = Encode(new
        {
            Version = 1, AdoptedAtUtc = DateTimeOffset.UtcNow, OriginalLoaderHash = Hash(currentLoader),
            OriginalLauncherHash = launcherHash, Reason = "User confirmed Steam game-file verification."
        });
        Commit(root, writes);
        return At(root, archive);
    }

    public static string[] Discover()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var basePath in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            if (!string.IsNullOrWhiteSpace(basePath)) libraries.Add(Path.Combine(basePath, "Steam"));
        foreach (var letter in new[] { "C", "D", "E", "F", "G" }) libraries.Add(letter + @":\SteamLibrary");
        foreach (var steam in libraries.ToArray())
        {
            try
            {
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return libraries.SelectMany(l => new[] { "Space Station 14 Playtest", "Space Station 14" }.Select(n => Path.Combine(l, "steamapps", "common", n, "bin_x64")))
            .Where(p => File.Exists(Path.Combine(p, Launcher)) && File.Exists(Path.Combine(p, Loader))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, byte[]?> SteamRestoreWrites(string root)
    {
        var writes = new Dictionary<string, byte[]?>();
        var state = ReadSteamState(root);
        if (state is null)
        {
            if (File.Exists(At(root, CleanLauncher))) throw Error("steam-backup-conflict", "An untracked clean launcher backup exists.");
            return writes;
        }
        ValidateSteamForRestore(root);
        writes[Launcher] = File.ReadAllBytes(At(root, CleanLauncher));
        writes[SteamRecord] = null;
        writes[CleanLauncher] = null;
        // The stable UI may currently be running. Leave its inert executable on disk.
        return writes;
    }

    private static void ValidateSteamForRestore(string root)
    {
        var state = ReadSteamState(root);
        if (state is null) return;
        if (!File.Exists(At(root, CleanLauncher)) || !Same(HashFile(At(root, CleanLauncher)), state.OriginalHash))
            throw Error("backup-invalid", "The original Steam launcher backup is missing or damaged.");
        var current = HashFile(At(root, Launcher));
        if (!Same(current, state.OriginalHash) && !Same(current, state.WrapperHash))
            throw Error("steam-changed", "Steam replaced or changed the launcher. Its current executable will not be overwritten.");
    }

    private static LoaderState? ReadLoaderState(string root)
    {
        if (!File.Exists(At(root, Record))) return null;
        var state = Read<LoaderState>(At(root, Record));
        if (!IsHash(state.OriginalHash) || !IsHash(state.PatchedHash) || state.Version is < 1 or > 2)
            throw Error("state-invalid", "The loader installation record is invalid.");
        if (state.OwnedFiles is not null)
            foreach (var (file, hash) in state.OwnedFiles)
            {
                _ = PayloadPath(file);
                if (!IsHash(hash)) throw Error("state-invalid", "The loader file record is invalid.");
            }
        return state;
    }

    private static SteamState? ReadSteamState(string root)
    {
        if (!File.Exists(At(root, SteamRecord))) return null;
        var state = Read<SteamState>(At(root, SteamRecord));
        if (!IsHash(state.OriginalHash) || !IsHash(state.WrapperHash) || state.Version != 1)
            throw Error("state-invalid", "The Steam integration record is invalid.");
        return state;
    }

    private static StableState? ReadStableState(string root)
    {
        if (!File.Exists(At(root, StableRecord))) return null;
        var state = Read<StableState>(At(root, StableRecord));
        if (state.Version != 1 || !IsHash(state.Hash)) throw Error("state-invalid", "The stable launcher record is invalid.");
        return state;
    }

    private static IntegrationPreferences? ReadIntegrationPreferences(string root)
    {
        if (!File.Exists(At(root, Preferences))) return null;
        var preferences = Read<IntegrationPreferences>(At(root, Preferences));
        if (preferences.Version != 1 || preferences.SteamIntegration is null)
            throw Error("state-invalid", "The Steam integration preference is invalid.");
        return preferences;
    }

    private static Dictionary<string, string> ReadOwnership(string root)
    {
        if (!File.Exists(At(root, Ownership))) return new(StringComparer.OrdinalIgnoreCase);
        var owned = Read<Dictionary<string, string>>(At(root, Ownership));
        foreach (var (file, hash) in owned)
        {
            _ = PayloadPath(file);
            if (!IsHash(hash)) throw Error("state-invalid", "The payload ownership record is invalid.");
        }
        return new(owned, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateBackup(string root, LoaderState state)
    {
        if (!File.Exists(At(root, Backup)) || !Same(HashFile(At(root, Backup)), state.OriginalHash))
            throw Error("backup-invalid", "The original loader backup is missing or damaged. Restore was stopped.");
    }

    private static byte[] EncodeSelection(string root, IReadOnlyList<string> files, string language, IReadOnlyDictionary<string, byte[]>? payload)
    {
        ValidateLanguage(language);
        if (files is null || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw Error("selection-invalid", "Select each mod only once.");
        var mods = new List<SelectedMod>();
        foreach (var file in files)
        {
            if (!IsModFile(file)) throw Error("selection-invalid", "A selected mod filename is invalid.");
            if (payload is not null && payload.TryGetValue(file, out var bytes)) mods.Add(new(file, Hash(bytes)));
            else
            {
                var path = At(root, PayloadPath(file));
                if (!File.Exists(path)) throw Error("mod-missing", $"The selected mod {file} is not installed.");
                mods.Add(new(file, HashFile(path)));
            }
        }
        return Encode(new ModSelection(1, language, mods));
    }

    private static void ValidateLanguage(string language)
    {
        if (language is not "ru" and not "en") throw Error("language-invalid", "Supported languages are ru and en.");
    }

    private static bool IsModFile(string? file) => file is not null && Regex.IsMatch(file, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\.Mod\.dll\z", RegexOptions.CultureInvariant)
        && file.Length <= 120 && !file.Contains("..");

    private static bool LooksLikeOriginalLauncher(byte[] bytes) => bytes.Length >= 64 && bytes[0] == 'M' && bytes[1] == 'Z'
        && bytes.AsSpan(0, Math.Min(bytes.Length, 1024 * 1024)).IndexOf("SS14.Launcher.dll"u8) >= 0;

    private static string PayloadPath(string file)
    {
        if (RuntimeFiles.Contains(file, StringComparer.Ordinal)) return Home + "/" + file;
        if (IsModFile(file)) return Home + "/Mods/" + file;
        throw Error("payload-invalid", "Only known runtime libraries and safe *.Mod.dll filenames are supported.");
    }

    private static byte[] Patch(byte[] original)
    {
        try
        {
            using var input = new MemoryStream(original, writable: false);
            using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { InMemory = true });
            var run = assembly.MainModule.GetType("SS14.Loader.Program")?.Methods.SingleOrDefault(m => m.Name == "Run" && m.Parameters.Count == 0);
            if (run?.HasBody != true || run.Body.Instructions.Count == 0) throw Error("loader-unsupported", "This SS14.Loader version is not supported.");
            if (run.Body.Instructions.Any(i => i.Operand is string text && text.Contains("SS14LocalMods", StringComparison.Ordinal)))
                throw Error("untracked-patch", "The source loader already contains a local-mod patch.");
            var module = assembly.MainModule;
            var il = run.Body.GetILProcessor();
            var first = run.Body.Instructions[0];
            // Preserve authentication and signature checks; initialize only the local mod bootstrap.
            var instructions = new[] {
                il.Create(OpCodes.Call, module.ImportReference(typeof(AppContext).GetProperty(nameof(AppContext.BaseDirectory))!.GetMethod!)),
                il.Create(OpCodes.Ldstr, "SS14LocalMods/SS14LocalMods.Bootstrap.dll"),
                il.Create(OpCodes.Call, module.ImportReference(typeof(Path).GetMethod(nameof(Path.Combine), [typeof(string), typeof(string)])!)),
                il.Create(OpCodes.Call, module.ImportReference(typeof(Assembly).GetMethod(nameof(Assembly.LoadFrom), [typeof(string)])!)),
                il.Create(OpCodes.Ldstr, "SS14LocalMods.Bootstrap"),
                il.Create(OpCodes.Callvirt, module.ImportReference(typeof(Assembly).GetMethod(nameof(Assembly.GetType), [typeof(string)])!)),
                il.Create(OpCodes.Ldstr, "Initialize"),
                il.Create(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetMethod), [typeof(string)])!)),
                il.Create(OpCodes.Ldnull), il.Create(OpCodes.Ldnull),
                il.Create(OpCodes.Callvirt, module.ImportReference(typeof(MethodBase).GetMethod(nameof(MethodBase.Invoke), [typeof(object), typeof(object[])])!)),
                il.Create(OpCodes.Pop)
            };
            foreach (var instruction in instructions) il.InsertBefore(first, instruction);
            using var output = new MemoryStream();
            assembly.Write(output);
            return output.ToArray();
        }
        catch (InstallationException) { throw; }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
        { throw new InstallationException("loader-unsupported", "This SS14.Loader version could not be patched safely.", ex); }
    }

    private static bool ContainsPatch(string path)
    {
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
            return assembly.MainModule.GetType("SS14.Loader.Program")?.Methods.Where(m => m.HasBody)
                .Any(m => m.Body.Instructions.Any(i => i.Operand is string text && text.Contains("SS14LocalMods", StringComparison.Ordinal))) == true;
        }
        catch (BadImageFormatException) { throw Error("loader-unsupported", "The loader is not a supported managed assembly."); }
    }

    private static string ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw Error("root-invalid", "Choose the folder containing SS14.Launcher.exe and loader/SS14.Loader.dll.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        EnsureNoLinks(root);
        if (!File.Exists(At(root, Launcher)) || !File.Exists(At(root, Loader)))
            throw Error("root-invalid", "Choose the folder containing SS14.Launcher.exe and loader/SS14.Loader.dll.");
        return root;
    }

    private static string At(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(x => x is ".." or "." or ""))
            throw Error("path-invalid", "An installation path is invalid.");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw Error("path-invalid", "An installation path escapes the selected folder.");
        EnsureNoLinks(path);
        return path;
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw Error("path-link", "Installation through junctions or symbolic links is not supported.");
        }
    }

    /// <summary>Read-only process preflight for setup UI; installation repeats this check before any write.</summary>
    public static void CheckNotRunning(string root) => EnsureNotRunning(ValidateRoot(root));

    private static void EnsureNotRunning(string root)
    {
        foreach (var name in new[] { "SS14.Loader", "Robust.Client", "SS14.Launcher", "SS14.Launcher.clean" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    string? path;
                    try { path = process.MainModule?.FileName; }
                    catch (InvalidOperationException) { continue; } // Already exited.
                    catch (System.ComponentModel.Win32Exception)
                    { throw Error("process-check-failed", "Close all SS14 clients and launchers before changing the installation."); }
                    if (path is not null && Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw Error("game-running", "Close the SS14 client and original launcher before changing this installation.");
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }

    private static IDisposable Lock(string root)
    {
        var name = (OperatingSystem.IsWindows() ? @"Local\" : "") + "SS14ModLauncher-" + Hash(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant()));
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw Error("installation-busy", "Another mod launcher is changing this installation.");
            return new MutexLease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private static void Commit(string root, IReadOnlyDictionary<string, byte[]?> writes)
    {
        if (writes.Count == 0) return;
        var journalPath = At(root, JournalDirectory);
        if (Directory.Exists(journalPath)) throw Error("interrupted-transaction", "A previous transaction requires recovery.");
        var entries = new List<JournalEntry>();
        Directory.CreateDirectory(journalPath);
        try
        {
            foreach (var (relative, after) in writes)
            {
                ValidateTransactionTarget(relative);
                var path = At(root, relative);
                if (Directory.Exists(path)) throw Error("path-invalid", "A directory occupies an installation file path.");
                var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (before is null && after is null || before is not null && after is not null && Same(Hash(before), Hash(after))) continue;
                var snapshot = before is null ? null : entries.Count + ".bak";
                if (before is not null) AtomicWrite(Path.Combine(journalPath, snapshot!), before);
                entries.Add(new(relative, before is null ? null : Hash(before), after is null ? null : Hash(after), snapshot));
            }
            AtomicWrite(Path.Combine(journalPath, "journal.json"), Encode(new Journal(1, entries)));
            foreach (var entry in entries)
            {
                var path = At(root, entry.Path);
                if (!SameNullable(HashIfExists(path), entry.BeforeHash)) throw Error("file-changed", "A file changed while preparing installation.");
                WriteOrDelete(path, writes[entry.Path]);
            }
            AtomicWrite(Path.Combine(journalPath, "committed"), [1]);
        }
        catch (Exception cause)
        {
            try { RecoverPending(root); }
            catch (Exception recovery) { throw new InstallationException("interrupted-transaction", "The operation stopped and needs recovery. Existing files and backups were preserved.", new AggregateException(cause, recovery)); }
            throw;
        }
        // A locked journal after a successful commit can be cleaned on the next mutation.
        try { CleanupJournal(root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void RecoverPending(string root)
    {
        var path = At(root, JournalDirectory);
        if (!Directory.Exists(path)) return;
        if (File.Exists(Path.Combine(path, "committed"))) { CleanupJournal(root); return; }
        var manifest = Path.Combine(path, "journal.json");
        if (!File.Exists(manifest)) { CleanupJournal(root); return; } // Snapshots staged; no writes started.
        var journal = Read<Journal>(manifest);
        if (journal.Version != 1 || journal.Entries is null || journal.Entries.Count > 1024) throw Error("state-invalid", "The recovery journal is invalid.");
        if (journal.Entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Entries.Count)
            throw Error("state-invalid", "The recovery journal contains duplicate paths.");
        // Validate every snapshot and target before rolling back even the first file.
        foreach (var entry in journal.Entries)
        {
            ValidateTransactionTarget(entry.Path);
            if (entry.BeforeHash is not null && !IsHash(entry.BeforeHash) || entry.AfterHash is not null && !IsHash(entry.AfterHash))
                throw Error("state-invalid", "The recovery journal contains an invalid hash.");
            if (entry.BeforeHash is not null)
            {
                if (entry.Snapshot is null || !Regex.IsMatch(entry.Snapshot, @"\A[0-9]+\.bak\z")) throw Error("state-invalid", "The recovery snapshot name is invalid.");
                var snapshot = At(root, JournalDirectory + "/" + entry.Snapshot);
                if (!File.Exists(snapshot) || !Same(HashFile(snapshot), entry.BeforeHash)) throw Error("backup-invalid", "A recovery snapshot is missing or damaged.");
            }
            var current = HashIfExists(At(root, entry.Path));
            if (!SameNullable(current, entry.BeforeHash) && !SameNullable(current, entry.AfterHash))
                throw Error("file-changed", "A file changed after the interrupted operation. It will not be overwritten.");
        }
        foreach (var entry in journal.Entries.Reverse())
        {
            var target = At(root, entry.Path);
            if (!SameNullable(HashIfExists(target), entry.BeforeHash))
                WriteOrDelete(target, entry.BeforeHash is null ? null : File.ReadAllBytes(At(root, JournalDirectory + "/" + entry.Snapshot)));
        }
        // Mark a fully rolled-back transaction resolved before deleting any snapshot.
        // A crash during cleanup must never look like an incomplete rollback.
        AtomicWrite(Path.Combine(path, "committed"), [1]);
        CleanupJournal(root);
    }

    private static void CleanupJournal(string root)
    {
        var path = At(root, JournalDirectory);
        if (!Directory.Exists(path)) return;
        var entries = Directory.GetFileSystemEntries(path);
        foreach (var file in entries)
        {
            var name = Path.GetFileName(file);
            EnsureNoLinks(file);
            if (Directory.Exists(file) || name is not "journal.json" and not "committed" && !Regex.IsMatch(name, @"\A[0-9]+\.bak\z") && !name.EndsWith(".atomic.tmp", StringComparison.Ordinal))
                throw Error("state-invalid", "The recovery directory contains an unexpected file.");
        }
        // Keep the committed marker until snapshots and manifest are gone.
        foreach (var file in entries.Where(f => Path.GetFileName(f) != "committed")) File.Delete(file);
        var marker = Path.Combine(path, "committed");
        if (File.Exists(marker)) File.Delete(marker);
        Directory.Delete(path, recursive: false);
    }

    private static void ValidateTransactionTarget(string relative)
    {
        if (relative is Loader or Record or Backup or Selection or Ownership or Launcher or CleanLauncher or StableLauncher or SteamRecord or StableRecord or Preferences) return;
        if (relative.StartsWith(Home + "/", StringComparison.Ordinal))
        {
            var file = relative[(Home.Length + 1)..];
            if (RuntimeFiles.Contains(file, StringComparer.Ordinal)) return;
            if (file.StartsWith("Mods/", StringComparison.Ordinal) && IsModFile(file[5..])) return;
            if (Regex.IsMatch(file, @"\AHistory/[0-9a-f]{32}/(?:installation\.json|SS14\.Loader\.original\.dll|steam\.json|SS14\.Launcher\.clean\.exe|adoption\.json)\z")) return;
        }
        throw Error("path-invalid", "The transaction references an unsupported file.");
    }

    private static void WriteOrDelete(string path, byte[]? bytes)
    {
        EnsureNoLinks(path);
        if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
        else AtomicWrite(path, bytes);
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EnsureNoLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".atomic.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static T Read<T>(string path)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json) ?? throw Error("state-invalid", "An installation record is empty."); }
        catch (JsonException ex) { throw new InstallationException("state-invalid", "An installation record is not valid JSON.", ex); }
    }
    private static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string? HashIfExists(string path) => File.Exists(path) ? HashFile(path) : null;
    private static bool IsHash(string? hash) => hash is not null && Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z");
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool SameNullable(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static InstallationException Error(string code, string message) => new(code, message);
}
