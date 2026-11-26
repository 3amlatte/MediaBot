using System.Buffers.Binary;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MediaRenamer.Services;

public record SubtitleResult(string Release, string Language, int Downloads, int FileId, string FileName);

public class OpenSubtitlesClient
{
    const string BaseUrl = "https://api.opensubtitles.com/api/v1";
    static readonly HttpClient Http = new();
    readonly string _apiKey;
    readonly string _user;
    readonly string _password;
    string? _token;

    public OpenSubtitlesClient(string apiKey, string user, string password)
    {
        _apiKey = apiKey;
        _user = user;
        _password = password;
    }

    public async Task<List<SubtitleResult>> SearchAsync(string language, string? query, int? season, int? episode,
        string? movieHash, long? movieSize)
    {
        var parts = new List<string> { "languages=" + Uri.EscapeDataString(language) };
        if (query is not null) parts.Add("query=" + Uri.EscapeDataString(query));
        if (season is int s) parts.Add("season_number=" + s);
        if (episode is int ep) parts.Add("episode_number=" + ep);
        if (movieHash is not null && movieSize is long size)
        {
            parts.Add("moviehash=" + movieHash);
            parts.Add("moviebytesize=" + size);
        }

        using var req = CreateRequest(HttpMethod.Get, "/subtitles?" + string.Join("&", parts));
        using var doc = JsonDocument.Parse(await SendAsync(req));

        var results = new List<SubtitleResult>();
        foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var a = d.GetProperty("attributes");
            if (!a.TryGetProperty("files", out var files) || files.GetArrayLength() == 0) continue;
            var f = files.EnumerateArray().First();
            results.Add(new SubtitleResult(
                a.TryGetProperty("release", out var rel) ? rel.GetString() ?? "" : "",
                a.TryGetProperty("language", out var lang) ? lang.GetString() ?? "" : "",
                a.TryGetProperty("download_count", out var dc) ? dc.GetInt32() : 0,
                f.GetProperty("file_id").GetInt32(),
                f.TryGetProperty("file_name", out var fn) ? fn.GetString() ?? "" : ""));
        }
        return results.OrderByDescending(r => r.Downloads).ToList();
    }

    public async Task<byte[]> DownloadAsync(int fileId)
    {
        await LoginAsync();
        using var req = CreateRequest(HttpMethod.Post, "/download", new { file_id = fileId });
        using var doc = JsonDocument.Parse(await SendAsync(req));
        var link = doc.RootElement.GetProperty("link").GetString()
            ?? throw new InvalidOperationException("No download link returned.");
        return await Http.GetByteArrayAsync(link);
    }

    public static (string Hash, long Size) ComputeHash(string path)
    {
        using var fs = File.OpenRead(path);
        long size = fs.Length;
        if (size < 128 * 1024) throw new IOException("File too small to hash.");

        ulong hash = (ulong)size;
        var buffer = new byte[64 * 1024];
        hash += SumBlocks(fs, buffer);
        fs.Seek(-buffer.Length, SeekOrigin.End);
        hash += SumBlocks(fs, buffer);
        return (hash.ToString("x16"), size);
    }

    static ulong SumBlocks(FileStream fs, byte[] buffer)
    {
        fs.ReadExactly(buffer);
        ulong sum = 0;
        for (int i = 0; i < buffer.Length; i += 8)
            sum += BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i, 8));
        return sum;
    }

    async Task LoginAsync()
    {
        if (_token is not null) return;
        using var req = CreateRequest(HttpMethod.Post, "/login", new { username = _user, password = _password });
        using var doc = JsonDocument.Parse(await SendAsync(req));
        _token = doc.RootElement.GetProperty("token").GetString();
    }

    HttpRequestMessage CreateRequest(HttpMethod method, string path, object? payload = null)
    {
        var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.Add("Api-Key", _apiKey);
        req.Headers.UserAgent.ParseAdd("MediaRenamer/1.0");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (payload is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return req;
    }

    static async Task<string> SendAsync(HttpRequestMessage req)
    {
        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenSubtitles error {(int)resp.StatusCode}: {body}");
        return body;
    }
}
