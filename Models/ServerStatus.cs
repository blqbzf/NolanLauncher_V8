namespace NolanWoWLauncher.Models;

public sealed class ServerStatus
{
    public bool Online { get; set; }
    public int OnlineCount { get; set; }
    public int PingMs { get; set; }
    public string ServerName { get; set; } = "";
    public string StatusText { get; set; } = "离线";

    public bool ReleaseOnline { get; set; }
    public bool TestOnline { get; set; }
    public int ReleasePingMs { get; set; }
    public int TestPingMs { get; set; }
    public string ReleaseSummary { get; set; } = "正式服离线";
    public string TestSummary { get; set; } = "测试服离线";
}
