using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MediaRenamer.Services;

public record UpdateInfo(Version Version, string Tag, string Url);

/// <summary>
/// Checks the public GitHub releases feed for a newer MediaBot version.
/// No account, key or telemetry is involved; it only reads the latest release.
/// </summary>
public static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/3amlatte/MediaBot/releases/latest";
    const string LatestReleaseApi = "https://api.github.com/repos/3amlatte/MediaBot/releases/latest";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaBot-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public static string DisplayVersion => CurrentVersion.ToString(3);

    /// <summary>
    /// Returns the newer release, or null when this build is up to date.
    /// Throws when GitHub can't be reached or returns something unexpected.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(LatestReleaseApi, ct);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return null;
        if (!root.TryGetProperty("tag_name", out var tagElement)) return null;

        var tag = tagElement.GetString() ?? "";
        if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var version)) return null;
        if (Normalize(version) <= Normalize(CurrentVersion)) return null;

        var url = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString()
            : null;

        return new UpdateInfo(version, tag, url ?? ReleasesPage);
    }

    public static void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No browser available; the release page is in the README.
        }
    }

    // Tags like "2.4.0" parse with a -1 revision; treat missing parts as zero.
    static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
