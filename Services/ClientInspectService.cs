using System;
using System.IO;
using System.Linq;
using NolanWoWLauncher.Models;

namespace NolanWoWLauncher.Services;

public sealed class ClientInspectService
{
    public ClientInspectResult Inspect(string clientPath)
    {
        var result = new ClientInspectResult();

        if (string.IsNullOrWhiteSpace(clientPath) || !Directory.Exists(clientPath))
        {
            result.StatusText = "尚未检测客户端";
            result.StatusColorHex = "#F0C97E";
            return result;
        }

        result.DirectoryExists = true;

        var exePath = GetGameExecutable(clientPath);
        var dataDir = Path.Combine(clientPath, "Data");
        var realmlist = Path.Combine(clientPath, "realmlist.wtf");
        var zhCN = Path.Combine(dataDir, "zhCN");
        var enCN = Path.Combine(dataDir, "enCN");

        result.ExecutablePath = exePath;
        result.HasExecutable = !string.IsNullOrWhiteSpace(exePath);
        result.HasDataDirectory = Directory.Exists(dataDir);
        result.HasRealmlist = File.Exists(realmlist);
        result.HasLocaleDirectory = Directory.Exists(zhCN) || Directory.Exists(enCN);
        result.HasPatchFiles = HasPatchFiles(dataDir);
        result.CacheBytes =
            GetDirectorySizeSafe(Path.Combine(clientPath, "Cache")) +
            GetDirectorySizeSafe(Path.Combine(dataDir, "Cache")) +
            GetDirectorySizeSafe(Path.Combine(clientPath, "WTF")) +
            GetDirectorySizeSafe(Path.Combine(clientPath, "Logs"));

        if (result.IsRecommendedState)
        {
            result.StatusText = "客户端完整，建议直接进入游戏";
            result.StatusColorHex = "#6DE68A";
        }
        else if (result.IsBasicallyComplete)
        {
            result.StatusText = "客户端基本完整";
            result.StatusColorHex = "#6DE68A";
        }
        else if (result.IsLaunchable)
        {
            result.StatusText = "客户端可启动，但仍需补全配置";
            result.StatusColorHex = "#F0C97E";
        }
        else
        {
            result.StatusText = "客户端目录不完整";
            result.StatusColorHex = "#FF7B7B";
        }

        return result;
    }

    public string? GetGameExecutable(string clientPath)
    {
        string[] candidates =
        {
            Path.Combine(clientPath, "Wow.exe"),
            Path.Combine(clientPath, "Wow-64.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private static bool HasPatchFiles(string dataDir)
    {
        if (!Directory.Exists(dataDir))
            return false;

        try
        {
            return Directory.EnumerateFiles(dataDir, "patch-*.MPQ", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(dataDir, "patch-*.mpq", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    private static long GetDirectorySizeSafe(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return 0;

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                })
                .Sum();
        }
        catch
        {
            return 0;
        }
    }
}
