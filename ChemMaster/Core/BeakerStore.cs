using System.Text.Json;

namespace SS14LocalMods.ChemMaster;

public sealed class BeakerStore(string path)
{
    private sealed record Document(int Version, BeakerTemperatures Temperatures);
    public BeakerTemperatures Load()
    {
        if (!File.Exists(path)) return new(500);
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException(Text.T("Файл температуры слишком большой.", "Beaker settings file is too large."));
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
        if (document is not { Version: 1, Temperatures.Valid: true }) throw new InvalidDataException(Text.T("Неверные настройки температуры.", "Invalid beaker settings."));
        return document.Temperatures;
    }
    public void Save(BeakerTemperatures temperatures)
    {
        if (!temperatures.Valid) throw new ArgumentException(Text.T("Неверная температура мензурок.", "Invalid beaker temperatures."));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document(1, temperatures)));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
