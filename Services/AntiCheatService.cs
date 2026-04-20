using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NolanWoWLauncher.Services;

public sealed class AntiCheatService
{
    // 常见外挂/脚本/多开工具进程名（部分）
    private static readonly string[] SuspiciousProcesses = new[]
    {
        // 按键精灵系列
        "KeyHook", "KeyWizard", "KeyWizard3", "KBuilder", "KPO",
        "zmrb", "zbrowser", "MacroMaker", "MacroExpert",
        "AutoKey", "AutoHotkey", "AHK", "KeySen",
        "神盾", "神盾客户端", "bsMain",

        // 多开/同步器
        "同步器", "多开器", "S同步", "E同步",
        "MultiBox", "Multi-Box", "MBox", "MBoxHost",
        "ISBoxer", "ISBoxerLite", "ISB", "LavishApp",
        "LBox", "LBoxHost", "LavishController",

        // 封包/协议工具
        "PacketEditor", "PacketTool", "WPE", "WPEPro",
        "CE", "CheatEngine", "Cheat Engine",
        "注入器", "Injector", "dllinject",

        // 变速/加速
        "变速齿轮", "变速器", "SpeedGear", "GameSpeed",
        "光速者", "光速", "MHS", "MemoryHackingSoftware",

        // 常见外挂宿主
        "TNT", "TNT登录器", "LOL外挂", "DNF外挂", "WOW外挂",
        "飞天", "无限位", "mem", "kernel_directx",
        "EE", "EAdLL", "EAC", "EasyAntiCheat",
        "BattlEye", "BEService", "BEClient",

        // 模拟器/虚拟机（部分）
        "vboxservice", "vboxtray", "vmtoolsd", "vmwaretray",
        "vmwareuser", "qemu-ga", "prl_vm_app",

        // 其他可疑
        "TopMost", "AlwaysOnTop", "WindowHider", "HideToolz",
        "ProcessHider", "hideprocess", "psexec",
        "Radmin", "RadminViewer", "DameWare",
    };

    private static readonly HashSet<string> ExcludedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "smss", "csrss", "wininit", "services",
        "lsass", "svchost", "fontdrvhost", "dwm", "winlogon",
        "explorer", "taskhostw", "RuntimeBroker", "ShellExperienceHost",
        "SearchIndexer", "SecurityHealthService", "MsMpEng",
        "NisSrv", "spoolsv", "WmiPrvSE", "dllhost",
        "conhost", "fontdrvhost", "sihost", "TcUI2",
        "ctfmon", "audiodg", "SearchHost", "StartMenuExperienceHost",
        "TextInputHost", "Widgets", "WidgetsHost", "SystemSettings",
        "DataExchangeHost", "合统", "armsint", "MsIdleForce",
    };

    private readonly string _reportEndpoint;
    private string? _sessionToken;
    private string? _machineFingerprint;

    public AntiCheatService()
    {
        _reportEndpoint = "http://43.248.129.172:88/api/anti-cheat/report";
    }

    /// <summary>
    /// 完整反挂检查，返回结果
    /// </summary>
    public AntiCheatResult FullCheck()
    {
        var result = new AntiCheatResult();

        // 1. 进程扫描
        result.SuspiciousProcesses = DetectSuspiciousProcesses();
        result.ProcessCheckPassed = result.SuspiciousProcesses.Count == 0;

        // 2. 机器指纹
        result.MachineFingerprint = GenerateMachineFingerprint();
        _machineFingerprint = result.MachineFingerprint;

        // 3. 启动令牌
        result.SessionToken = GenerateSessionToken();
        _sessionToken = result.SessionToken;

        // 4. 登录器自身完整性（可选，检查 embedded 资源哈希）
        result.LauncherIntegrityOk = true; // 后续可对比内置哈希

        result.OverallPassed = result.ProcessCheckPassed && result.LauncherIntegrityOk;

        return result;
    }

    /// <summary>
    /// 轻量级检查（EnterGame 前快速调用），只扫描进程
    /// </summary>
    public List<string> QuickProcessScan()
    {
        return DetectSuspiciousProcesses();
    }

    /// <summary>
    /// 生成进入游戏的令牌（一次性，随机数 + 时间戳 + 机器指纹）
    /// </summary>
    public string GenerateSessionToken()
    {
        var raw = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{_machineFingerprint ?? GenerateMachineFingerprint()}_{RandomHex(16)}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 上报反挂数据到服务器（异步，不阻塞启动）
    /// </summary>
    public async Task ReportAsync(AntiCheatResult result, string clientPath, string realmHost)
    {
        try
        {
            var payload = new
            {
                token = result.SessionToken,
                fingerprint = result.MachineFingerprint,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                clientPathHash = ComputeSimpleHash(clientPath),
                realmHost = realmHost,
                suspiciousProcesses = result.SuspiciousProcesses,
                processCheckPassed = result.ProcessCheckPassed,
                overallPassed = result.OverallPassed,
                launcherIntegrityOk = result.LauncherIntegrityOk,
            };

            using var http = new HttpClient(new HttpClientHandler { Proxy = null, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 不等待响应，只管发
            _ = http.PostAsync(_reportEndpoint, content);
        }
        catch
        {
            // 上报失败不影响游戏启动
        }
    }

    /// <summary>
    /// 扫描可疑进程
    /// </summary>
    public List<string> DetectSuspiciousProcesses()
    {
        var found = new List<string>();

        try
        {
            // 用 Win32_Process 而不是 Process.GetProcesses()，避免权限问题漏掉
            using var searcher = new ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Process");
            using var results = searcher.Get();

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ManagementObject mo in results)
            {
                var name = mo["Name"] as string;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                name = name.Trim();
                if (seenNames.Contains(name))
                    continue;
                seenNames.Add(name);

                // 跳过系统进程
                var baseName = Path.GetFileNameWithoutExtension(name);
                if (ExcludedSystemProcesses.Contains(baseName))
                    continue;

                // 匹配可疑进程名
                foreach (var suspect in SuspiciousProcesses)
                {
                    if (name.Equals(suspect, StringComparison.OrdinalIgnoreCase) ||
                        baseName.Equals(suspect, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(suspect, StringComparison.OrdinalIgnoreCase))
                    {
                        var pid = mo["ProcessId"]?.ToString() ?? "?";
                        found.Add($"{name} (PID: {pid})");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 权限不足时静默跳过，不阻止启动
            System.Diagnostics.Debug.WriteLine($"[AntiCheat] Process scan error: {ex.Message}");
        }

        return found.Distinct().ToList();
    }

    /// <summary>
    /// 生成机器指纹（组合 CPU、主板、MAC、磁盘序列号）
    /// </summary>
    public string GenerateMachineFingerprint()
    {
        try
        {
            var sb = new StringBuilder();

            // CPU ID
            sb.Append(GetWmiProperty("Win32_Processor", "ProcessorId", "CPU"));
            // 主板序列号
            sb.Append(GetWmiProperty("Win32_BaseBoard", "SerialNumber", "MB"));
            // 磁盘序列号（系统盘）
            sb.Append(GetDiskSerialNumber());
            // MAC 地址（第一个物理网卡）
            sb.Append(GetFirstMacAddress());

            var raw = sb.ToString().Replace("-", "").Replace(" ", "").Replace(":", "");
            if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8)
                raw = Guid.NewGuid().ToString("N");

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
        catch
        {
            return Guid.NewGuid().ToString("N")[..16].ToLowerInvariant();
        }
    }

    private static string GetWmiProperty(string scope, string property, string fallback)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {scope}");
            using var results = searcher.Get();
            foreach (ManagementObject mo in results)
            {
                var val = mo[property] as string;
                if (!string.IsNullOrWhiteSpace(val))
                    return val.Trim();
            }
        }
        catch { }
        return fallback;
    }

    private static string GetDiskSerialNumber()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SerialNumber FROM Win32_PhysicalMedia");
            using var results = searcher.Get();
            foreach (ManagementObject mo in results)
            {
                var sn = mo["SerialNumber"] as string;
                if (!string.IsNullOrWhiteSpace(sn))
                    return sn.Trim();
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='C:'");
            using var results = searcher.Get();
            foreach (ManagementObject mo in results)
            {
                var sn = mo["VolumeSerialNumber"] as string;
                if (!string.IsNullOrWhiteSpace(sn))
                    return sn.Trim();
            }
        }
        catch { }

        return "DISK_UNKNOWN";
    }

    private static string GetFirstMacAddress()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;
                var mac = ni.GetPhysicalAddress().ToString();
                if (!string.IsNullOrWhiteSpace(mac) && mac.Length >= 6)
                    return mac;
            }
        }
        catch { }
        return "MAC_UNKNOWN";
    }

    private static string RandomHex(int length)
    {
        var bytes = new byte[length / 2 + 1];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToHexString(bytes)[..length].ToLowerInvariant();
    }

    private static string ComputeSimpleHash(string input)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}

public class AntiCheatResult
{
    public bool ProcessCheckPassed { get; set; }
    public bool LauncherIntegrityOk { get; set; }
    public bool OverallPassed { get; set; }
    public List<string> SuspiciousProcesses { get; set; } = new();
    public string? MachineFingerprint { get; set; }
    public string? SessionToken { get; set; }
}
