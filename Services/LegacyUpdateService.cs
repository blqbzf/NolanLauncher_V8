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
        // 清理启动器遗留的 .tmp/.tMP 临时文件
        CleanTempPatchFiles(clientPath);

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

    public async Task<bool> IsPatchUpToDateAsync(string clientPath, bool useTestServer)
    {
        var missing = await GetMissingOrOutdatedAsync(clientPath, useTestServer);
        return missing.Count == 0;
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
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    File.Delete(localPath);
                    break;
                }
                catch (IOException ex) when (i < 4)
                {
                    progress?.Report(((i + 1) * 15, $"正在重试覆盖 {patch.Name}", $"文件被占用，等待重试 ({i + 1}/5)…"));
                    await Task.Delay(1000, cancellationToken);
                }
                catch (IOException ex)
                {
                    // 5次都失败，尝试用 cmd 强制删除
                    try
                    {
                        var tmpDel = localPath + ".del.bat";
                        await File.WriteAllTextAsync(tmpDel, $"@echo off\necho y | del /f /q \"{localPath}\"\ndel /f /q \"%~f0\"\n", cancellationToken);
                        var p = Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"{tmpDel}\"", UseShellExecute = false, CreateNoWindow = true });
                        p?.WaitForExit(5000);
                        if (!File.Exists(localPath)) break;
                    } catch { }
                    throw new IOException($"文件被其他进程占用，无法覆盖补丁。请确认 Wow.exe 已完全关闭（检查任务管理器），然后重试。", ex);
                }
            }
        }

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

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler
        {
            Proxy = null,
            UseProxy = false
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
    private static string AppendTs(string url) => url + (url.Contains('?') ? '&' : '?') + "ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static string? GetString(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

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

                // patch-zhCN-Z.mpq.tmp -> patch-zhCN-Z.mpq
                var baseName = Path.GetFileNameWithoutExtension(file);
                // 去掉可能的多重后缀如 patch-zhCN-Z.mpq.tmp -> patch-zhCN-Z
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
                    // 移动失败则直接删除临时文件
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
