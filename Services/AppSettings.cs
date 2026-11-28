using System.Text.Json;

namespace MediaRenamer.Services;

public record AppSettings(
    string ApiKey = "",
    string TvTemplate = "{n} - S{s}E{e} - {t}",
    string MovieTemplate = "{n} ({y})",
    string OutputFolder = "",
    string TvFolder = "{n}/Season {s}",
    string MovieFolder = "{n} ({y})",
    bool WriteMetadata = false,
    bool DryRun = false,
    string OpenSubsApiKey = "",
    string OpenSubsUser = "",
    string OpenSubsPassword = "",
    string SubtitleLanguage = "en",
    string SonarrUrl = "",
    string SonarrApiKey = "",
    bool SonarrIncludeSpecials = false,
    bool SonarrMonitoredOnly = false,
    string MetadataSource = "tmdb",
    string TvdbApiKey = "",
    string TvdbPin = "",
    bool CheckUpdates = true);

public static class SettingsStore
{
    static readonly string FilePath = AppPaths.DataFile("settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
    }
}
