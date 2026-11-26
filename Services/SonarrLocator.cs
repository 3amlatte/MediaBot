using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace MediaRenamer.Services;

public record LocalSonarr(string Url, string ApiKey);

public static class SonarrLocator
{
    public static LocalSonarr? Detect()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var folder in folders)
        {
            var file = Path.Combine(folder, "Sonarr", "config.xml");
            if (!File.Exists(file)) continue;

            try
            {
                var root = XDocument.Load(file).Root;
                var apiKey = root?.Element("ApiKey")?.Value.Trim();
                if (root is null || string.IsNullOrEmpty(apiKey)) continue;

                bool ssl = string.Equals(root.Element("EnableSsl")?.Value, "True", StringComparison.OrdinalIgnoreCase);
                var port = ssl ? root.Element("SslPort")?.Value : root.Element("Port")?.Value;
                var urlBase = (root.Element("UrlBase")?.Value ?? "").Trim('/');

                var url = $"{(ssl ? "https" : "http")}://localhost:{port ?? "8989"}";
                if (urlBase.Length > 0) url += "/" + urlBase;

                return new LocalSonarr(url, apiKey);
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
