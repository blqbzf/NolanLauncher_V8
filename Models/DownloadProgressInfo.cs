namespace NolanWoWLauncher.Models;

public sealed class DownloadProgressInfo
{
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public double ProgressPercent { get; set; }
    public string SpeedText { get; set; } = "";
    public string StatusText { get; set; } = "";
}
