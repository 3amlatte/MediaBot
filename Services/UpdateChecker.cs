using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MediaRenamer.Services;

public record UpdateAsset(string Name, string Url, string? Sha256);

public record UpdateInfo(Version Version, string Tag, string PageUrl, UpdateAsset? Msi, UpdateAsset? Portable);

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

        var page = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString()
            : null;

        UpdateAsset? msi = null;
        UpdateAsset? portable = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var downloadElement)
                    ? downloadElement.GetString()
                    : null;
                if (string.IsNullOrEmpty(url)) continue;

                var digest = asset.TryGetProperty("digest", out var digestElement)
                    ? digestElement.GetString()
                    : null;
                if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    digest = digest["sha256:".Length..];

                var item = new UpdateAsset(name, url, string.IsNullOrWhiteSpace(digest) ? null : digest);

                if (name.EndsWith("-x64.msi", StringComparison.OrdinalIgnoreCase)) msi = item;
                else if (name.EndsWith("-portable-x64.zip", StringComparison.OrdinalIgnoreCase)) portable = item;
            }
        }

        return new UpdateInfo(version, tag, page ?? ReleasesPage, msi, portable);
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

    // Tags like "2.5.0" parse with a -1 revision; treat missing parts as zero.
    static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
