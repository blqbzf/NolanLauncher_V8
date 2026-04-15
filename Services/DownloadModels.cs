namespace NolanWoWLauncher.Services;

public sealed class DownloadConfig
{
    public string DownloadUrl { get; set; } = "";
    public string PatchBaseUrl { get; set; } = "http://43.248.129.172:88/downloads/";
    public string OutputFileName { get; set; } = "诺兰时光客户端.zip";
    public string Description { get; set; } = "客户端下载";
}

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}
