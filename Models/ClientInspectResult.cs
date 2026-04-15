namespace NolanWoWLauncher.Models;

public sealed class ClientInspectResult
{
    public bool DirectoryExists { get; set; }
    public bool HasExecutable { get; set; }
    public bool HasDataDirectory { get; set; }
    public bool HasRealmlist { get; set; }
    public bool HasLocaleDirectory { get; set; }
    public bool HasPatchFiles { get; set; }

    public bool IsLaunchable => DirectoryExists && HasExecutable && HasDataDirectory;
    public bool IsBasicallyComplete => IsLaunchable && HasRealmlist;
    public bool IsRecommendedState => IsBasicallyComplete && HasLocaleDirectory && HasPatchFiles;

    public string? ExecutablePath { get; set; }
    public long CacheBytes { get; set; }
    public string StatusText { get; set; } = "尚未检测客户端";
    public string StatusColorHex { get; set; } = "#F0C97E";
}
