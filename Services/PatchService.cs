using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using NolanWoWLauncher.Models;

namespace NolanWoWLauncher.Services;

public sealed class PatchService
{
    private const string ReleaseManifestUrl = "http://43.248.129.172:88/patches-channels/combined-release-manifest.json";

    public IReadOnlyList<PatchItem> LoadPatchItems(string? clientPath = null)
    {
        // 清理旧版启动器遗留的 .tmp/.tMP
        if (!string.IsNullOrWhiteSpace(clientPath) && Directory.Exists(clientPath))
            CleanTempPatchFiles(clientPath);

        var items = LoadManifest();

        if (string.IsNullOrWhiteSpace(clientPath) || !Directory.Exists(clientPath))
            return items;

        foreach (var item in items)
        {
            var localRelative = !string.IsNullOrWhiteSpace(item.RelativePath)
                ? item.RelativePath!
                : GetLegacyRelativePath(item.FileName);

            if (string.IsNullOrWhiteSpace(localRelative))
                continue;

            var fullPath = Path.Combine(clientPath, localRelative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            bool exists = File.Exists(fullPath);

            if (exists)
            {
                item.State = "已安装";
                item.StateColor = "#6DE68A";
            }
            else if (item.Required)
            {
                item.State = "缺失";
                item.StateColor = "#FF7B7B";
            }
            else
            {
                item.State = "可更新";
                item.StateColor = "#FFB86B";
            }
        }

        return items;
    }

    private List<PatchItem> LoadManifest()
    {
        var remote = LoadReleaseChannelManifest();
        if (remote.Count > 0)
            return remote;

        try
        {
            var json = EmbeddedResources.ReadTextOrNull("patch_manifest.json");
            var items = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<List<PatchItem>>(json);

            return items is { Count: > 0 } ? items : GetDefaultItems();
        }
        catch
        {
            return GetDefaultItems();
        }
    }

    private static List<PatchItem> LoadReleaseChannelManifest()
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { Proxy = null, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };
            var url = ReleaseManifestUrl + "?ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var json = http.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var list = new List<PatchItem>();

            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var name = GetString(e, "Name") ?? "服务器补丁";
                var downloadUrl = GetString(e, "DownloadUrl") ?? GetString(e, "Url") ?? string.Empty;
                var localRelative = GetString(e, "LocalRelativePath") ?? string.Empty;
                var fileName = !string.IsNullOrWhiteSpace(localRelative)
                    ? Path.GetFileName(localRelative)
                    : (!string.IsNullOrWhiteSpace(downloadUrl) ? Path.GetFileName(new Uri(downloadUrl).AbsolutePath) : name);
                var size = e.TryGetProperty("Size", out var sizeEl) && sizeEl.TryGetInt64(out var bytes)
                    ? ClientInspectService.FormatBytes(bytes)
                    : "自动检测";

                list.Add(new PatchItem
                {
                    Name = name,
                    Version = GetString(e, "Version") ?? "正式服",
                    Size = size,
                    State = "待校验",
                    StateColor = "#F0C97E",
                    FileName = fileName,
                    RelativePath = localRelative,
                    DownloadUrl = downloadUrl,
                    Hash = GetString(e, "Sha256") ?? GetString(e, "Hash"),
                    Required = !e.TryGetProperty("Required", out var req) || req.GetBoolean()
                });
            }

            return list;
        }
        catch
        {
            return new List<PatchItem>();
        }
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string GetLegacyRelativePath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        return string.Equals(Path.GetExtension(fileName), ".mpq", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine("Data", "zhCN", "patch-z.MPQ")
            : Path.Combine("Data", "zhCN", fileName);
    }

    private static List<PatchItem> GetDefaultItems()
    {
        return
        [
            new PatchItem
            {
                Name = "服务器补丁",
                Version = "正式服",
                Size = "自动检测",
                State = "待校验",
                StateColor = "#F0C97E",
                FileName = "patch-zhCN-Z.mpq",
                RelativePath = "Data/zhCN/patch-zhCN-Z.mpq",
                DownloadUrl = "http://43.248.129.172:88/patches/shared/patch-zhCN-Z.mpq",
                Required = true
            }
        ];
    }

    /// <summary>
    /// 清理旧版启动器下载补丁时遗留的 .tmp/.tMP 临时文件，并尝试重命名为正式 .mpq
    /// </summary>
    private static void CleanTempPatchFiles(string clientPath)
    {
        try
        {
            var zhCnDir = Path.Combine(clientPath, "Data", "zhCN");
            if (!Directory.Exists(zhCnDir)) return;

            var tempExts = new[] { ".tmp", ".tMP", ".TMP", ".temp" };
            foreach (var file in Directory.GetFiles(zhCnDir, "patch-*.*"))
            {
                var ext = Path.GetExtension(file);
                if (Array.IndexOf(tempExts, ext) < 0) continue;

                var baseName = Path.GetFileNameWithoutExtension(file);
                if (baseName.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase))
                    baseName = baseName[..^4];

                var targetPath = Path.Combine(zhCnDir, baseName + ".mpq");
                try
                {
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);
                    File.Move(file, targetPath);
                }
                catch
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
