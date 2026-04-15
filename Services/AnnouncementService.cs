using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NolanWoWLauncher.Models;

namespace NolanWoWLauncher.Services;

public sealed class AnnouncementService
{
    public async Task<AnnouncementModel> LoadAsync(string url, LauncherConfig config)
    {
        try
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<AnnouncementModel>(json);

            if (result is not null)
                return result;
        }
        catch
        {
        }

        return new AnnouncementModel
        {
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Title = config.AnnouncementTitle,
            Body = config.AnnouncementBody,
            Line1 = "◆ 冰冠堡垒开放，副本内容持续更新",
            Line2 = "◆ 冬幕节活动进行中，登录即可参与",
            Line3 = "◆ 支持一键修复 Realmlist 与客户端清理",
            Line4 = "◆ 建议首次进入前先进行补丁检查"
        };
    }
}
