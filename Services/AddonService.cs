using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace NolanWoWLauncher.Services;

public sealed class AddonService
{
    private const string AddonZipUrl = "http://43.248.129.172:88/patches/shared/NolanAddons.zip";

    private static readonly HttpClient Http = new(new HttpClientHandler { Proxy = null, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Download and install addons every time to ensure latest version.
    /// </summary>
    public static async Task<bool> EnsureAddonsAsync(string clientPath, Action<string>? log = null)
    {
        try
        {
            // clientPath might be the game root or the Interface folder itself
            var addonsDir = clientPath;
            if (!clientPath.EndsWith("Interface", StringComparison.OrdinalIgnoreCase))
                addonsDir = Path.Combine(clientPath, "Interface", "AddOns");
            else
                addonsDir = Path.Combine(clientPath, "AddOns");
            Directory.CreateDirectory(addonsDir);

            // Clean up old AIO folder (renamed to AIO_Client)
            var oldAioDir = Path.Combine(addonsDir, "AIO");
            if (Directory.Exists(oldAioDir))
            {
                try { Directory.Delete(oldAioDir, true); } catch { }
            }

            log?.Invoke("[插件] 正在检查插件更新...");

            // Download zip
            var zipBytes = await Http.GetByteArrayAsync(AddonZipUrl + "?ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // Extract zip to AddOns directory
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                var destPath = Path.Combine(addonsDir, entry.FullName);
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (entry.Length > 0)
                {
                    entry.ExtractToFile(destPath, true);
                }
                else if (!Directory.Exists(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }
            }

            log?.Invoke("[插件] AIO + NolanUnid 已更新到最新版本");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[插件] 插件更新失败：{ex.Message}（不影响游戏启动）");
            return false;
        }
    }
}
