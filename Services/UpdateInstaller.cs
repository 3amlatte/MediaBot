using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace MediaRenamer.Services;

/// <summary>
/// Downloads a release asset and hands installation to a small helper script
/// that waits for MediaBot to exit, then replaces the app and restarts it.
/// </summary>
public static class UpdateInstaller
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaBot-UpdateInstaller");
        return client;
    }

    public static async Task<string> DownloadAsync(UpdateAsset asset, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "MediaBot-update");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder))
        {
            try { File.Delete(old); } catch { /* not fatal */ }
        }

        var name = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(name)) name = "MediaBot-update.bin";
        var file = Path.Combine(folder, name);

        using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(file))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                if (total is > 0) progress?.Report((double)received / total.Value);
            }
        }

        if (asset.Sha256 is not null)
        {
            string hash;
            await using (var stream = File.OpenRead(file))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));

            if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidDataException("The downloaded file failed its checksum check.");
            }
        }

        progress?.Report(1);
        return file;
    }

    /// <summary>
    /// Starts the detached updater. The caller should exit right after this returns.
    /// </summary>
    public static void Launch(string package, bool portable)
    {
        var target = AppContext.BaseDirectory;
        var exe = Path.Combine(target, "MediaBot.exe");
        var script = Path.Combine(Path.GetTempPath(), $"MediaBot-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, Script, new UTF8Encoding(false));

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-WaitPid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("-Package");
        start.ArgumentList.Add(package);
        start.ArgumentList.Add("-Target");
        start.ArgumentList.Add(target);
        start.ArgumentList.Add("-Exe");
        start.ArgumentList.Add(exe);
        start.ArgumentList.Add("-Mode");
        start.ArgumentList.Add(portable ? "portable" : "msi");

        Process.Start(start);
    }

    const string Script = """
        param(
            [int]$WaitPid,
            [string]$Package,
            [string]$Target,
            [string]$Exe,
            [string]$Mode
        )

        $ErrorActionPreference = 'Stop'
        try {
            $app = Get-Process -Id $WaitPid -ErrorAction SilentlyContinue
            if ($app) { $app.WaitForExit(120000) | Out-Null }
            Start-Sleep -Milliseconds 800

            if ($Mode -eq 'portable') {
                Expand-Archive -LiteralPath $Package -DestinationPath $Target -Force
            } else {
                $msiArgs = @('/i', ('"' + $Package + '"'), '/passive', '/norestart')
                $install = Start-Process -FilePath msiexec.exe -ArgumentList $msiArgs -Verb RunAs -Wait -PassThru
                if ($install.ExitCode -ne 0 -and $install.ExitCode -ne 3010 -and $install.ExitCode -ne 1641) {
                    throw "The installer returned code $($install.ExitCode)."
                }
            }

            Start-Process -FilePath $Exe
        } catch {
            "MediaBot update failed: $($_.Exception.Message)" |
                Set-Content -LiteralPath (Join-Path $env:TEMP 'MediaBot-update-error.txt')
            Start-Process -FilePath $Exe
        } finally {
            Remove-Item -LiteralPath $Package -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
        }
        """;
}
