namespace NolanWoWLauncher.Models;

public sealed class LauncherConfig
{
    public string RealmHost { get; set; } = "43.248.129.172";
    public string RegisterUrl { get; set; } = "https://你的注册网址";
    public string ServerDisplayName { get; set; } = "正式服 / 巫妖王之怒 / 3.3.5a";
    public string LauncherVersion { get; set; } = "V4";
    public string LastClientPath { get; set; } = string.Empty;

    public string AnnouncementTitle { get; set; } = "欢迎来到诺兰时光";
    public string AnnouncementBody { get; set; } = "启动器已完成基础功能接入，可进行下载、修复、检测与启动。";

    public string ServerStatusUrl { get; set; } = "http://43.248.129.172:88/api/server-status.json";
    public string AnnouncementUrl { get; set; } = "http://43.248.129.172:88/api/announcement.json";
}
