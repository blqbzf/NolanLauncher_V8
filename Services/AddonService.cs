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
        { Timeout = TimeSpan.FromSeconds(60) };

    public static async Task<bool> EnsureAddonsAsync(string clientPath, Action<string>? log = null)
    {
        try
        {
            var addonsDir = clientPath;
            if (!clientPath.EndsWith("Interface", StringComparison.OrdinalIgnoreCase))
                addonsDir = Path.Combine(clientPath, "Interface", "AddOns");
            else
                addonsDir = Path.Combine(clientPath, "AddOns");
            Directory.CreateDirectory(addonsDir);

            log?.Invoke("[插件] 正在下载插件更新...");
            var zipBytes = await Http.GetByteArrayAsync(AddonZipUrl + "?ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            log?.Invoke($"[插件] 下载完成 ({zipBytes.Length / 1024}KB)");

            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            int updated = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var destPath = Path.Combine(addonsDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using var entryStream = entry.Open();
                using var fs = File.Create(destPath);
                await entryStream.CopyToAsync(fs);
                updated++;
            }

            log?.Invoke($"[插件] 插件更新完成 ({updated}个文件)");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[插件] 插件更新失败：{ex.Message}（不影响游戏启动）");
            return false;
        }
    }
}
