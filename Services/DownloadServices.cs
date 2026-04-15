using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NolanWoWLauncher.Models;

namespace NolanWoWLauncher.Services;

public sealed class DownloadServices
{
    public DownloadConfig LoadConfig()
    {
        var json = EmbeddedResources.ReadTextOrNull("download_config.json");
        return string.IsNullOrWhiteSpace(json)
            ? new DownloadConfig()
            : JsonSerializer.Deserialize<DownloadConfig>(json) ?? new DownloadConfig();
    }

    public async Task<OperationResult> DownloadClientAsync(
        string outputDirectory,
        Action<DownloadProgressInfo>? progressCallback = null)
    {
        var cfg = LoadConfig();

        if (string.IsNullOrWhiteSpace(cfg.DownloadUrl))
            return new OperationResult { Success = false, Message = "download_config.json 未配置 DownloadUrl" };

        Directory.CreateDirectory(outputDirectory);

        var outFile = Path.Combine(
            outputDirectory,
            string.IsNullOrWhiteSpace(cfg.OutputFileName) ? "诺兰时光客户端.zip" : cfg.OutputFileName);

        using var http = new HttpClient();
        using var response = await http.GetAsync(cfg.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(outFile);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        var stopwatch = Stopwatch.StartNew();

        while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await output.WriteAsync(buffer, 0, read);
            totalRead += read;

            double percent = 0;
            if (totalBytes.HasValue && totalBytes.Value > 0)
                percent = totalRead * 100d / totalBytes.Value;

            double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.1);
            double speedBytes = totalRead / seconds;

            progressCallback?.Invoke(new DownloadProgressInfo
            {
                BytesReceived = totalRead,
                TotalBytes = totalBytes,
                ProgressPercent = percent,
                SpeedText = FormatBytes((long)speedBytes) + "/s",
                StatusText = totalBytes.HasValue
                    ? $"{FormatBytes(totalRead)} / {FormatBytes(totalBytes.Value)}"
                    : $"{FormatBytes(totalRead)}"
            });
        }

        return new OperationResult
        {
            Success = true,
            Message = $"下载完成：{outFile}"
        };
    }

    private static string FormatBytes(long bytes)
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
}
