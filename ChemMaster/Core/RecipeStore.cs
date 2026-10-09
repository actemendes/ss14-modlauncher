using System.Text.Json;

namespace SS14LocalMods.ChemMaster;

public sealed record SavedRecipe(string Name, TargetMode Mode, Target[] Targets);
public sealed class RecipeStore(string path)
{
    private sealed record Document(int Version, SavedRecipe[] Recipes);
    public SavedRecipe[] Load()
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 262144) throw new InvalidDataException(Text.T("Файл наборов слишком большой.", "Recipe file is too large."));
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path)) ?? throw new InvalidDataException(Text.T("Неверный файл наборов.", "Invalid recipe file."));
        if (document.Version != 1 || document.Recipes == null || document.Recipes.Length > 128 || document.Recipes.Any(r => !Valid(r)) ||
            document.Recipes.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Recipes.Length)
            throw new InvalidDataException(Text.T("Неподдерживаемый или неверный файл наборов.", "Unsupported or invalid recipe file."));
        return document.Recipes;
    }
    public void Save(SavedRecipe recipe)
    {
        if (!Valid(recipe)) throw new ArgumentException(Text.T("Укажите название и хотя бы одну цель.", "Enter a name and at least one target."));
        var recipes = Load().Where(r => !r.Name.Equals(recipe.Name, StringComparison.OrdinalIgnoreCase)).Append(recipe)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (recipes.Length > 128) throw new InvalidDataException(Text.T("Не более 128 наборов.", "Recipe limit: 128."));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document(1, recipes), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static bool Valid(SavedRecipe? r) => r != null && !string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 80 &&
        Enum.IsDefined(r.Mode) && r.Targets is { Length: > 0 and <= 64 } && r.Targets.All(t => t != null &&
            !string.IsNullOrWhiteSpace(t.Id) && t.Id.Length <= 128 && t.Amount is > 0 and <= 10000000);
}
