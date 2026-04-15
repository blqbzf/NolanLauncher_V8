using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using NolanWoWLauncher.Models;

namespace NolanWoWLauncher.Services;

public sealed class ServerStatusService
{
    public async Task<ServerStatus> LoadAsync(string host, string fallbackServerName)
    {
        var auth = await CheckPortAsync(host, 3724);
        var world = await CheckPortAsync(host, 8085);
        bool online = auth.Online && world.Online;

        return new ServerStatus
        {
            Online = online,
            OnlineCount = online ? 1 : 0,
            PingMs = world.PingMs,
            ServerName = fallbackServerName,
            StatusText = online ? "正式服在线" : "正式服离线",
            ReleaseOnline = online,
            ReleasePingMs = world.PingMs,
            ReleaseSummary = $"正式服 {(online ? "在线" : "离线")} (3724/8085)",
            TestOnline = false,
            TestPingMs = 0,
            TestSummary = string.Empty
        };
    }

    private static async Task<(bool Online, int PingMs)> CheckPortAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            var connectTask = client.ConnectAsync(host, port);
            var finished = await Task.WhenAny(connectTask, Task.Delay(1500));
            if (finished != connectTask || !client.Connected)
                return (false, 0);

            sw.Stop();
            return (true, (int)Math.Max(sw.ElapsedMilliseconds, 1));
        }
        catch
        {
            return (false, 0);
        }
    }
}
