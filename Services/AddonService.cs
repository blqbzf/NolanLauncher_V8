using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using War3Net.IO.Mpq;

namespace NolanWoWLauncher.Services;

public sealed class AddonService
{
    // Addon files to extract from MPQ
    private static readonly string[] AddonMpqPaths = {
        "Interface\\AddOns\\NolanUnid\\NolanUnid.lua",
        "Interface\\AddOns\\NolanUnid\\NolanUnid.toc",
    };

    /// <summary>
    /// Extract addon files from local MPQ patch file.
    /// </summary>
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

            // Find local patch-Z.mpq
            var patchPath = Path.Combine(clientPath, "patch-Z.mpq");
            if (!File.Exists(patchPath))
            {
                log?.Invoke("[插件] 未找到本地补丁文件，跳过插件提取");
                return true;
            }

            log?.Invoke("[插件] 正在从本地MPQ补丁提取插件...");

            int updated = 0;

            using var fs = File.OpenRead(patchPath);
            using var archive = new MpqArchive(fs, true);

            // Add filenames so we can find files by path
            foreach (var mpqPath in AddonMpqPaths)
                archive.AddFileName(mpqPath);

            foreach (var mpqPath in AddonMpqPaths)
            {
                if (!archive.FileExists(mpqPath))
                {
                    log?.Invoke($"[插件] MPQ中未找到: {Path.GetFileName(mpqPath)}");
                    continue;
                }

                // Convert MPQ path to disk path
                var relativePath = mpqPath.Substring("Interface\\AddOns\\".Length);
                var destPath = Path.Combine(addonsDir, relativePath.Replace('\\', Path.DirectorySeparatorChar));

                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Extract file from MPQ
                using var mpqStream = archive.OpenFile(mpqPath);
                using var outFs = File.Create(destPath);
                await mpqStream.CopyToAsync(outFs);

                updated++;
                log?.Invoke($"[插件] 已提取: {Path.GetFileName(mpqPath)}");
            }

            log?.Invoke($"[插件] 完成 (更新{updated}个文件)");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[插件] 插件更新失败：{ex.Message}（不影响游戏启动）");
            return false;
        }
    }
}
