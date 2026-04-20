using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace NolanWoWLauncher.Services;

public sealed class AddonService
{
    private const string AddonZipUrl = "http://43.248.129.172:88/patches/shared/NolanAddons.zip";
    // Folders managed by this ZIP — will be fully cleaned before extraction
    private static readonly string[] ManagedFolders = { "AIO_Client", "NolanUnid" };

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

            log?.Invoke("[插件] 检查插件更新...");

            // Download ZIP with cache-bust
            var zipBytes = await Http.GetByteArrayAsync(AddonZipUrl + "?ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            log?.Invoke($"[插件] 下载完成 ({zipBytes.Length / 1024}KB)");

            // Compute ZIP content hash for change detection
            using var sha = System.Security.Cryptography.SHA256.Create();
            var zipHash = Convert.ToHexString(sha.ComputeHash(zipBytes)).ToLowerInvariant();

            // Check if ZIP changed since last update
            var hashFile = Path.Combine(addonsDir, ".nolan_addons_hash");
            var lastHash = File.Exists(hashFile) ? File.ReadAllText(hashFile).Trim() : "";

            if (lastHash == zipHash)
            {
                log?.Invoke("[插件] 插件已是最新，无需更新");
                return true;
            }

            // ZIP is different — clean managed folders and extract
            log?.Invoke("[插件] 检测到插件更新，正在替换...");
            foreach (var folder in ManagedFolders)
            {
                var dir = Path.Combine(addonsDir, folder);
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }

            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            int extracted = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var destPath = Path.Combine(addonsDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using var entryStream = entry.Open();
                using var fs = File.Create(destPath);
                await entryStream.CopyToAsync(fs);
                extracted++;
            }

            // Save hash
            await File.WriteAllTextAsync(hashFile, zipHash);

            log?.Invoke($"[插件] 插件更新完成 ({extracted}个文件)");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[插件] 插件更新失败：{ex.Message}（不影响游戏启动）");
            return false;
        }
    }
}
