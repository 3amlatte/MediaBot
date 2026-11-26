using System.Text.Json;

namespace MediaRenamer.Services;

public static class UndoHistory
{
    const int MaxBatches = 50;
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaRenamer", "undo.json");

    public static List<RenameBatch> Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<RenameBatch>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public static void Save(List<RenameBatch> batches)
    {
        while (batches.Count > MaxBatches) batches.RemoveAt(0);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(batches));
    }
}
