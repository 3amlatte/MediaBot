using System.IO;
using System.Text;
using MediaRenamer.Models;

namespace MediaRenamer.Services;

public static class SubtitleMatcher
{
    public static string? FindLocal(RenameItem video)
    {
        var dir = Path.GetDirectoryName(video.SourcePath)!;
        if (!Directory.Exists(dir)) return null;

        var subs = Directory.EnumerateFiles(dir)
            .Where(f => Renamer.SubtitleExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        var byName = subs
            .Where(f => StartsWithStem(Path.GetFileNameWithoutExtension(f), video.Stem))
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f).Length)
            .FirstOrDefault();
        if (byName is not null) return byName;

        var p = video.Parsed;
        if (p.Season is null || p.Episode is null) return null;
        return subs.FirstOrDefault(f =>
        {
            var sp = FilenameParser.Parse(f);
            return sp.Season == p.Season && sp.Episode == p.Episode;
        });
    }

    public static string Apply(RenameItem video, string subPath, bool convertToUtf8)
    {
        var target = Path.Combine(Path.GetDirectoryName(video.SourcePath)!, TargetName(video, subPath));
        bool sameFile = string.Equals(target, subPath, StringComparison.OrdinalIgnoreCase);
        if (!sameFile)
        {
            if (File.Exists(target) && !string.Equals(target, subPath, StringComparison.Ordinal))
                throw new IOException("A file already exists: " + Path.GetFileName(target));
            File.Move(subPath, target, overwrite: false);
        }

        if (convertToUtf8) ConvertToUtf8(target);
        return target;
    }

    public static void ConvertToUtf8(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return;

        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else
            text = DecodeUtf8OrLatin1(bytes);

        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    static string DecodeUtf8OrLatin1(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    static bool StartsWithStem(string subStem, string videoStem)
    {
        if (!subStem.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase)) return false;
        if (subStem.Length == videoStem.Length) return true;
        return !char.IsLetterOrDigit(subStem[videoStem.Length]);
    }

    static string TargetName(RenameItem video, string subPath)
    {
        var subStem = Path.GetFileNameWithoutExtension(subPath);
        var ext = Path.GetExtension(subPath);
        string suffix;

        if (StartsWithStem(subStem, video.Stem))
        {
            suffix = subStem[video.Stem.Length..];
        }
        else
        {
            var sp = FilenameParser.Parse(subPath);
            suffix = sp.TagEnd >= 0 ? subStem[sp.TagEnd..] : "";
        }

        return video.Stem + suffix + ext;
    }
}
