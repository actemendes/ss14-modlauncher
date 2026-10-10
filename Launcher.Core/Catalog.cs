using System.Text.Json;

namespace SS14ModLauncher.Core;

public sealed record ModDefinition
{
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    public string MinLauncherVersion { get; init; } = "0.1.2";
    public string File { get; init; } = "";
    public string NameRu { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string DescriptionRu { get; init; } = "";
    public string DescriptionEn { get; init; } = "";
    public string Category { get; init; } = "";
    public string InstructionsRu { get; init; } = "";
    public string InstructionsEn { get; init; } = "";
    public string? PreviewImage { get; init; }
    public string? Source { get; init; }
    public string Name(string language) => language == "ru" ? NameRu : NameEn;
    public string Description(string language) => language == "ru" ? DescriptionRu : DescriptionEn;
    public string Instructions(string language) => language == "ru" ? InstructionsRu : InstructionsEn;
    public string CategoryName(string language) => (Category, language == "ru") switch
    {
        ("interface", true) => "Интерфейс", ("interface", false) => "Interface",
        ("communication", true) => "Общение", ("communication", false) => "Communication",
        ("automation", true) => "Автоматизация", ("automation", false) => "Automation",
        ("demo", true) => "Тестирование", ("demo", false) => "Testing",
        (_, true) => "Другое", _ => "Other"
    };
}

public static class Catalog
{
    public const string LauncherVersion = "0.1.8";
    public static IReadOnlyList<ModDefinition> Bundled { get; } = ReadBundled();
    public static ModDefinition? ById(string id) => Bundled.FirstOrDefault(m => m.Id == id);

    public static bool IsSafeModFileName(string? name) => name is { Length: > 8 and <= 120 }
        && name.EndsWith(".Mod.dll", StringComparison.Ordinal)
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
        && !name.StartsWith('.') && !name.Contains("..", StringComparison.Ordinal);

    private static IReadOnlyList<ModDefinition> ReadBundled()
    {
        using var resource = typeof(Catalog).Assembly.GetManifestResourceStream("SS14ModLauncher.Core.mods.json")
            ?? throw new InvalidDataException("Embedded mod catalog is missing.");
        var mods = JsonSerializer.Deserialize<List<ModDefinition>>(resource,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Embedded mod catalog is empty.");
        if (mods.Count == 0 || mods.Any(m => string.IsNullOrWhiteSpace(m.Id) || !IsSafeModFileName(m.File)
                || !SemanticVersion.TryParse(m.Version, out _) || !SemanticVersion.TryParse(m.MinLauncherVersion, out _))
            || mods.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != mods.Count
            || mods.Select(m => m.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mods.Count)
            throw new InvalidDataException("Embedded mod catalog is invalid.");
        return mods.AsReadOnly();
    }
}
