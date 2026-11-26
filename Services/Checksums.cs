using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MediaRenamer.Services;

public record ChecksumEntry(string FileName, string Expected);

public static class Checksums
{
    public static readonly string[] Algorithms = { "crc32", "md5", "sha1", "sha256" };

    static readonly Regex EmbeddedRx = new(@"\[([0-9A-Fa-f]{8})\]", RegexOptions.Compiled);
    static readonly uint[] Table = BuildTable();

    public static string? AlgorithmForFile(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".sfv" => "crc32",
        ".md5" => "md5",
        ".sha1" => "sha1",
        ".sha256" => "sha256",
        _ => null
    };

    public static string Extension(string algo) => algo switch
    {
        "crc32" => ".sfv",
        "md5" => ".md5",
        "sha1" => ".sha1",
        _ => ".sha256"
    };

    public static string? EmbeddedCrc(string fileName)
    {
        var m = EmbeddedRx.Match(Path.GetFileName(fileName));
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static string Compute(string path, string algo)
    {
        using var fs = File.OpenRead(path);
        if (algo == "crc32") return Crc32(fs);

        byte[] hash = algo switch
        {
            "md5" => MD5.HashData(fs),
            "sha1" => SHA1.HashData(fs),
            _ => SHA256.HashData(fs)
        };
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static List<ChecksumEntry> ParseFile(string path)
    {
        var algo = AlgorithmForFile(path);
        var entries = new List<ChecksumEntry>();

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;

            if (algo == "crc32")
            {
                int idx = line.LastIndexOf(' ');
                if (idx <= 0) continue;
                entries.Add(new ChecksumEntry(line[..idx].Trim(), line[(idx + 1)..].Trim()));
            }
            else
            {
                int idx = line.IndexOf(' ');
                if (idx <= 0) continue;
                var name = line[(idx + 1)..].Trim().TrimStart('*');
                entries.Add(new ChecksumEntry(name, line[..idx].Trim()));
            }
        }

        return entries;
    }

    public static string FormatLine(string algo, string fileName, string hash) =>
        algo == "crc32" ? $"{fileName} {hash.ToUpperInvariant()}" : $"{hash}  {fileName}";

    static string Crc32(Stream stream)
    {
        uint crc = 0xFFFFFFFF;
        var buffer = new byte[1 << 16];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        }
        return (crc ^ 0xFFFFFFFF).ToString("X8");
    }

    static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
