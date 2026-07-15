using System.Text.Json;
using System.IO;
using Voltaris.Models;

namespace Voltaris.Services;

public sealed class StateStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltaris");

    private string StatePath => Path.Combine(_directory, "state.json");

    public PersistedState Load()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(StatePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save(PersistedState state)
    {
        Directory.CreateDirectory(_directory);
        var temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, StatePath, true);
    }
}
