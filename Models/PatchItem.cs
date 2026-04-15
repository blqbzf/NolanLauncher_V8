namespace NolanWoWLauncher.Models;

public sealed class PatchItem
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Size { get; set; } = "";
    public string State { get; set; } = "";
    public string StateColor { get; set; } = "#7FDBFF";
    public string? FileName { get; set; }
    public bool Required { get; set; }
    public string? RelativePath { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Hash { get; set; }
}
