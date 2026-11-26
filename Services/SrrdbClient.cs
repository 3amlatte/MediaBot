using System.Net.Http;
using System.Text.Json;

namespace MediaRenamer.Services;

public record SrrdbRelease(string Name, string Date, long Size, string? ImdbId);

public record SrrdbFile(string Name, long Size, string Crc);

public record SrrdbDetails(string Name, List<SrrdbFile> Files);

/// <summary>
/// Read-only client for the public srrDB API v1 (https://api.srrdb.com/v1/docs).
/// The API asks users not to scrape, so requests are sent one at a time with a short delay,
/// and only when the user explicitly asks for a lookup.
/// </summary>
public class SrrdbClient
{
    const string BaseUrl = "https://api.srrdb.com/v1";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<List<SrrdbRelease>> SearchByCrcAsync(string crc) =>
        await SearchAsync($"archive-crc:{crc}");

    public async Task<List<SrrdbRelease>> SearchByNameAsync(string name) =>
        await SearchAsync($"r:{name}");

    public async Task<List<SrrdbRelease>> SearchAsync(string query)
    {
        using var doc = await GetAsync($"search/{Uri.EscapeDataString(query)}");
        var results = new List<SrrdbRelease>();
        if (!doc.RootElement.TryGetProperty("results", out var list)) return results;

        foreach (var r in list.EnumerateArray())
        {
            var name = Str(r, "release");
            if (name.Length == 0) continue;
            results.Add(new SrrdbRelease(
                name,
                Str(r, "date"),
                r.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var size) ? size : 0,
                StrOrNull(r, "imdbId")));
        }
        return results;
    }

    public async Task<SrrdbDetails?> DetailsAsync(string releaseName)
    {
        using var doc = await GetAsync($"details/{Uri.EscapeDataString(releaseName)}");
        var root = doc.RootElement;
        var name = Str(root, "name");
        if (name.Length == 0) return null;

        var files = new List<SrrdbFile>();
        if (root.TryGetProperty("archived-files", out var archived))
        {
            foreach (var f in archived.EnumerateArray())
            {
                var fileName = Str(f, "name");
                var crc = Str(f, "crc");
                if (fileName.Length == 0 || crc.Length == 0) continue;
                long size = f.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var v) ? v : 0;
                files.Add(new SrrdbFile(fileName, size, crc));
            }
        }
        return new SrrdbDetails(name, files);
    }

    async Task<JsonDocument> GetAsync(string path)
    {
        await Gate.WaitAsync();
        try
        {
            await Task.Delay(300);

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{path}");
            req.Headers.UserAgent.ParseAdd("MediaBot/2.0 (personal media manager)");

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException("Could not reach srrdb.com: " + ex.Message);
            }

            using (resp)
            {
                var body = await resp.Content.ReadAsStringAsync();
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return JsonDocument.Parse("{}");
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"srrdb.com returned HTTP {(int)resp.StatusCode}.");
                return JsonDocument.Parse(body);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string? StrOrNull(JsonElement e, string name)
    {
        var s = Str(e, name);
        return s.Length == 0 ? null : s;
    }
}
