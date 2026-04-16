using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using NolanWoWLauncher.Models;

namespace NolanWoWLauncher.Services;

public sealed class LegacyUpdateService
{
    private const string ReleaseManifestUrl = "http://43.248.129.172:88/patches-channels/combined-release-manifest.json";
    private const string ReleaseVersionUrl = "http://43.248.129.172:88/patches-channels/combined-release-version.json";
    private const string TestManifestUrl = "http://43.248.129.172:88/patches-channels/combined-test-manifest.json";
    private const string TestVersionUrl = "http://43.248.129.172:88/patches-channels/combined-test-version.json";
    private readonly string _appDataPath = EmbeddedResources.AppDataDir;
    private string UpdateRecordPath => Path.Combine(_appDataPath, "UpdatedFiles.json");

    public async Task<string> GetVersionAsync(bool useTestServer)
    {
        using var http = CreateHttp();
        var json = await http.GetStringAsync(AppendTs(useTestServer ? TestVersionUrl : ReleaseVersionUrl));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "未知"
            : "未知";
    }

    public async Task<List<PatchItem>> GetPatchesAsync(bool useTestServer)
    {
        using var http = CreateHttp();
        var json = await http.GetStringAsync(AppendTs(useTestServer ? TestManifestUrl : ReleaseManifestUrl));
        using var doc = JsonDocument.Parse(json);
        var result = new List<PatchItem>();

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var downloadUrl = GetString(e, "DownloadUrl") ?? GetString(e, "Url") ?? string.Empty;
            var localRelative = GetString(e, "LocalRelativePath") ?? string.Empty;
            var fileName = !string.IsNullOrWhiteSpace(localRelative)
                ? Path.GetFileName(localRelative)
                : (!string.IsNullOrWhiteSpace(downloadUrl) ? Path.GetFileName(new Uri(downloadUrl).AbsolutePath) : GetString(e, "Name") ?? "patch");

            result.Add(new PatchItem
            {
                Name = GetString(e, "Name") ?? fileName,
                Version = GetString(e, "Version") ?? "正式服",
                Size = e.TryGetProperty("Size", out var sizeEl) && sizeEl.TryGetInt64(out var bytes)
                    ? ClientInspectService.FormatBytes(bytes)
                    : "自动检测",
                FileName = fileName,
                RelativePath = localRelative,
                DownloadUrl = downloadUrl,
                Hash = GetString(e, "Sha256") ?? GetString(e, "Hash"),
                Required = !e.TryGetProperty("Required", out var req) || req.GetBoolean(),
                State = "待校验",
                StateColor = "#F0C97E"
            });
        }

        return result;
    }

    public async Task<List<PatchItem>> GetPatchStatusAsync(string clientPath, bool useTestServer)
    {
        var patches = await GetPatchesAsync(useTestServer);

        foreach (var patch in patches)
        {
            var relative = string.IsNullOrWhiteSpace(patch.RelativePath)
                ? Path.Combine("Data", "zhCN", patch.FileName ?? string.Empty)
                : patch.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var localPath = Path.Combine(clientPath, relative);
            patch.State = $"校验中: path={localPath}";

            if (!File.Exists(localPath))
            {
                patch.State = $"缺失 | path={localPath}";
                patch.StateColor = "#FF7B7B";
                continue;
            }

            if (!string.IsNullOrWhiteSpace(patch.Hash))
            {
                var hash = await ComputeSha256Async(localPath);
                if (!hash.Equals(patch.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    patch.State = $"可更新 | local={hash} manifest={patch.Hash}";
                    patch.StateColor = "#FFB86B";
                    continue;
                }
            }

            patch.State = !string.IsNullOrWhiteSpace(patch.Hash)
                ? $"已安装 | hash={patch.Hash}"
                : "已安装";
            patch.StateColor = "#6DE68A";
        }

        return patches;
    }

    public async Task<List<PatchItem>> GetMissingOrOutdatedAsync(string clientPath, bool useTestServer)
    {
        var patches = await GetPatchStatusAsync(clientPath, useTestServer);
        return patches.Where(p => p.State == "缺失" || p.State == "可更新" || p.State == "待校验").ToList();
    }

    public async Task<bool> DownloadPatchAsync(
        PatchItem patch,
        string clientPath,
        IProgress<(double Percent, string Status, string Detail)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var relative = string.IsNullOrWhiteSpace(patch.RelativePath)
            ? Path.Combine("Data", "zhCN", patch.FileName ?? string.Empty)
            : patch.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var localPath = Path.Combine(clientPath, relative);
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        using var http = CreateHttp();
        using var response = await http.GetAsync(patch.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        byte[] downloadedBytes;
        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var memory = new MemoryStream())
        {
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            var sw = Stopwatch.StartNew();

            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;
                var percent = total > 0 ? readTotal * 100d / total : 0;
                var speed = readTotal / Math.Max(sw.Elapsed.TotalSeconds, 0.1);
                progress?.Report((percent, $"正在下载 {patch.Name} ({percent:0.0}%)", $"{ClientInspectService.FormatBytes(readTotal)} / {(total > 0 ? ClientInspectService.FormatBytes(total) : "未知")}  {ClientInspectService.FormatBytes((long)speed)}/s"));
            }

            downloadedBytes = memory.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(patch.Hash))
        {
            using var sha = SHA256.Create();
            var actual = Convert.ToHexString(sha.ComputeHash(downloadedBytes)).ToLowerInvariant();
            if (!actual.Equals(patch.Hash, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"补丁校验失败：{patch.Name}");
        }

        if (File.Exists(localPath))
            File.Delete(localPath);

        await File.WriteAllBytesAsync(localPath, downloadedBytes, cancellationToken);

        if (!File.Exists(localPath))
            throw new Exception($"补丁覆盖失败：{localPath}");

        progress?.Report((100, $"正在完成 {patch.Name}", $"已校验完成并覆盖正式文件：{localPath}"));

        if (File.Exists(localPath) && !string.IsNullOrWhiteSpace(patch.FileName) && patch.FileName.Contains("patch-zhCN-Z", StringComparison.OrdinalIgnoreCase))
        {
            var compatTargets = new[]
            {
                Path.Combine(clientPath, "Data", "zhCN", "patch-z.MPQ"),
                Path.Combine(clientPath, "Data", "patch-z.MPQ")
            };

            foreach (var compat in compatTargets)
            {
                var compatDir = Path.GetDirectoryName(compat);
                if (!string.IsNullOrWhiteSpace(compatDir))
                    Directory.CreateDirectory(compatDir);
                File.Copy(localPath, compat, true);
            }
        }

        await SaveCompletedFileAsync(localPath, patch.Hash ?? string.Empty);
        return true;
    }

    private async Task SaveCompletedFileAsync(string filePath, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return;
        Dictionary<string, string> data;
        if (File.Exists(UpdateRecordPath))
        {
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(UpdateRecordPath)) ?? new();
            }
            catch
            {
                data = new();
            }
        }
        else
        {
            data = new();
        }

        data[filePath] = hash;
        await File.WriteAllTextAsync(UpdateRecordPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpClient CreateHttp() => new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string AppendTs(string url) => url + (url.Contains('?') ? '&' : '?') + "ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static string? GetString(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
