using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace MediaRenamer.Services;

public static class ImageLoader
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Dictionary<string, BitmapImage> Cache = new();

    public static async Task<BitmapImage?> LoadAsync(string url)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(url, out var cached)) return cached;
        }

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            lock (Cache)
            {
                Cache[url] = image;
            }
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return null;
        }
    }
}
