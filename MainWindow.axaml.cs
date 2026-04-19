using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NolanWoWLauncher.Models;
using NolanWoWLauncher.Services;

namespace NolanWoWLauncher;

public partial class MainWindow : Window
{
    private static string LogPath => Path.Combine(EmbeddedResources.AppDataDir, "startup.log");

    private readonly DownloadServices _downloadServices = new();
    private readonly RepairServices _repairServices = new();
    private readonly ClientInspectService _clientInspectService = new();
    private readonly PatchService _patchService = new();
    private readonly LegacyUpdateService _legacyUpdateService = new();
    private readonly LauncherConfigService _launcherConfigService = new();
    private readonly ServerStatusService _serverStatusService = new();
    private readonly AnnouncementService _announcementService = new();

    private readonly LauncherConfig _launcherConfig;
    private bool _useTestServer;

    private DispatcherTimer? _enterGameParticleTimer;
    private readonly Random _particleRandom = new();

    private readonly Dictionary<Button, ButtonVisualState> _buttonVisualStates = new();
    private readonly HashSet<Button> _wiredSoundButtons = new();
    private RegisterWindow? _registerWindow;

    public MainWindow()
    {
        _launcherConfig = _launcherConfigService.Load();

        SafeAppendFileLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow ctor enter{Environment.NewLine}");

        InitializeComponent();

        SafeAppendFileLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] InitializeComponent ok{Environment.NewLine}");

        Opened += OnOpened;

        BindMinimizeButton();
        BindButtonsSafely();
        WireWoWButtonEffects();
        RestoreLastClientPath();
    }

    private void BindMinimizeButton()
    {
        var minimizeButton = FindCtrl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }
        else
        {
            AppendLog("未找到按钮：MinimizeButton");
        }
    }

    private void BindButtonsSafely()
    {
        BindButton("BrowseClientButton", BrowseClientFolderAsync);
        BindButton("FixRealmlistButton", FixRealmlistAsync);
        BindButton("ClearCacheButton", ClearCacheAsync);
        BindButton("RepairClientFullButton", DeepCleanClientAsync);
        BindRepairButtonHard();
        BindButton("EnterGameButton", EnterGameAsync);

        BindButton("RegisterButton", OpenRegisterWindowAsync);

        // 检查更新：先按正式服 manifest + SHA256 判断是否需要更新；没有新补丁时不重复下载。
        BindButton("CheckUpdateButton", () => PatchUpdateAsync(forceDownload: false));
        BindOptionalButton("PatchUpdateButton", () => PatchUpdateAsync(forceDownload: false));
        BindButton("DownloadClientButton", DownloadClientAsync);

        BindButton("WoWDatabaseButton", () =>
        {
            OpenExternalUrl(GetDatabaseUrl());
            return Task.CompletedTask;
        });

        BindButton("ExitGameButton", () =>
        {
            Close();
            return Task.CompletedTask;
        });

        var exitButton = FindCtrl<Button>("ExitLauncherButton");
        if (exitButton is not null)
        {
            exitButton.Click += (_, _) => Close();
        }
        else
        {
            AppendLog("未找到按钮：ExitLauncherButton");
        }

        BindButton("RefreshAnnouncementButton", async () =>
        {
            await RefreshServerStatusAsync();
            await RefreshAnnouncementAsync();
        });
    }

    private void BindButton(string name, Func<Task> action)
    {
        var button = FindCtrl<Button>(name);
        if (button is null)
        {
            AppendLog($"未找到按钮：{name}");
            return;
        }

        button.Click += async (_, _) =>
        {
            try
            {
                AppendLog($"按钮点击：{name}");
                await action();
            }
            catch (Exception ex)
            {
                AppendLog($"按钮事件异常 {name}：{ex.Message}");
            }
        };
    }

    private void BindRepairButtonHard()
    {
        var button = FindCtrl<Button>("RepairClientFullButton");
        if (button is null)
        {
            AppendLog("硬绑定失败：未找到 RepairClientFullButton");
            return;
        }

        button.PointerPressed += async (_, _) =>
        {
            AppendLog("硬绑定触发：修复客户端按钮 PointerPressed");
            await DeepCleanClientAsync();
        };
    }

    private void BindOptionalButton(string name, Func<Task> action)
    {
        var button = FindCtrl<Button>(name);
        if (button is null)
            return;

        button.Click += async (_, _) =>
        {
            try
            {
                AppendLog($"按钮点击：{name}");
                await action();
            }
            catch (Exception ex)
            {
                AppendLog($"按钮事件异常 {name}：{ex.Message}");
            }
        };
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            TryLoadBackground();
            LoadHeroImage();
            LoadInitialUi();
            StartEnterGameParticles();
            AppendLog("启动器界面已加载。");

            await RefreshServerStatusAsync();
            await RefreshAnnouncementAsync();
            await CheckUpdateAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"OnOpened 异常：{ex}");
        }
    }

    private string CurrentPatchChannel => _useTestServer ? "combined-test" : "combined-release";
    private string CurrentServerName => _useTestServer ? "测试服" : "正式服";

    private void WireServerSelector()
    {
        var releaseButton = FindCtrl<Button>("ReleaseServerButton");
        var testButton = FindCtrl<Button>("TestServerButton");

        if (releaseButton is not null)
        {
            releaseButton.Click += async (_, _) =>
            {
                _useTestServer = false;
                ApplyServerProfile();
                await CheckUpdateAsync();
            };
        }

        if (testButton is not null)
        {
            testButton.Click += async (_, _) =>
            {
                _useTestServer = true;
                ApplyServerProfile();
                await CheckUpdateAsync();
            };
        }
    }

    private void ApplyServerProfile()
    {
        SetText("CurrentServerText", _useTestServer ? "测试服 / 巫妖王之怒 / 3.3.5a" : _launcherConfig.ServerDisplayName);
        SetText("RealmlistText", $"set realmlist {_launcherConfig.RealmHost}");
        SetText("PatchChannelText", CurrentPatchChannel);
        SetText("OnlineCountText", _useTestServer ? "在线分区：测试服" : "在线分区：正式服");
        SetServerButtonVisual("ReleaseServerButton", !_useTestServer);
        SetServerButtonVisual("TestServerButton", _useTestServer);
        AppendLog($"已切换服务器配置：{CurrentServerName}");
    }

    private void SetServerButtonVisual(string buttonName, bool selected)
    {
        var button = FindCtrl<Button>(buttonName);
        if (button is null)
            return;

        button.Opacity = selected ? 1.0 : 0.78;
        button.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#F1C86A" : "#7A6A42"));
        button.BorderThickness = new Thickness(selected ? 2 : 1);
    }

    private void LoadInitialUi()
    {
        SetText("BottomVersionText", $"Nolan Launcher   {_launcherConfig.LauncherVersion}");
        SetText("ServerAddressText", _launcherConfig.RealmHost);
        SetText("CurrentServerText", _launcherConfig.ServerDisplayName);
        WireServerSelector();
        ApplyServerProfile();
        SetText("ClientVersionText", "等待检测");
        SetText("ServerStatusText", "加载中...");
        SetText("ServerOnlineText", "检测中...");
        SetText("OnlineStatusText", "● 检测中...");
        SetText("OnlineCountText", "在线人数：--");
        SetText("PingText", "延迟 -- ms");
        SetText("StatusBarText", "状态：等待选择客户端目录");

        SetText("AnnouncementUpdatedText", "最近更新：--");
        SetText("AnnouncementTitleText", _launcherConfig.AnnouncementTitle);
        SetText("AnnouncementBodyText", _launcherConfig.AnnouncementBody);
        SetText("AnnouncementLine1", "◆ 正在加载公告详情...");
        SetText("AnnouncementLine2", string.Empty);
        SetText("AnnouncementLine3", string.Empty);
        SetText("AnnouncementLine4", string.Empty);

        SetText("RealmlistText", $"set realmlist {_launcherConfig.RealmHost}");
        SetText("PatchChannelText", CurrentPatchChannel);
        SetText("PatchCountText", "补丁列表（0）");
        SetText("DownloadProgressText", "等待下载");
        SetText("DownloadSpeedText", "-");

        var clientPath = GetTextBoxText("ClientPathBox");
        InspectClientPath(clientPath);
    }

    private async Task RefreshServerStatusAsync()
    {
        try
        {
            var status = await _serverStatusService.LoadAsync(
                _launcherConfig.RealmHost,
                _launcherConfig.ServerDisplayName);

            SetText("OnlineStatusText", status.ReleaseSummary);
            SetForeground("OnlineStatusText", status.Online ? "#67E06E" : "#FF6B6B");

            SetText("OnlineCountText", status.Online ? "在线分区：正式服" : "在线分区：无");
            SetText("PingText", status.Online
                ? $"正式服 {status.ReleasePingMs}ms"
                : "延迟 -- ms");

            SetText("ServerOnlineText", status.StatusText);
            SetForeground("ServerOnlineText", status.Online ? "#67E06E" : "#FF6B6B");

            SetText("ServerStatusText", status.StatusText);
            SetForeground("ServerStatusText", status.Online ? "#D3A14A" : "#FF6B6B");
        }
        catch (Exception ex)
        {
            AppendLog($"刷新服务器状态失败：{ex.Message}");
            SetText("ServerStatusText", "异常");
            SetForeground("ServerStatusText", "#FF6B6B");
        }
    }

    private async Task BrowseClientFolderAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null)
            {
                AppendLog("无法打开目录选择器。");
                return;
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "选择魔兽世界客户端目录",
                    AllowMultiple = false
                });

            var folder = folders.FirstOrDefault();
            if (folder is null)
            {
                AppendLog("已取消选择客户端目录。");
                return;
            }

            var path = folder.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AppendLog("选中的目录不是本地目录。");
                return;
            }

            SetTextBoxText("ClientPathBox", path);
            _launcherConfig.LastClientPath = path;
            _launcherConfigService.Save(_launcherConfig);
            InspectClientPath(path);
            AppendLog($"已选择客户端目录：{path}");
        }
        catch (Exception ex)
        {
            AppendLog($"选择目录失败：{ex.Message}");
        }
    }

    private void RestoreLastClientPath()
    {
        try
        {
            var path = _launcherConfig.LastClientPath?.Trim();
            if (string.IsNullOrWhiteSpace(path))
                return;

            SetTextBoxText("ClientPathBox", path);
            InspectClientPath(path);
        }
        catch (Exception ex)
        {
            AppendLog($"恢复上次客户端目录失败：{ex.Message}");
        }
    }

    private void InspectClientPath(string path)
    {
        try
        {
            var result = _clientInspectService.Inspect(path);

            SetForeground("ClientIntegrityDot", result.StatusColorHex);
            SetText("ClientIntegrityText", result.StatusText);
            SetForeground("ClientIntegrityText", result.StatusColorHex);
            SetText("CacheSizeText", $"缓存：{ClientInspectService.FormatBytes(result.CacheBytes)}");
            SetText("ClientVersionText", result.StatusText);
            SetForeground("ClientVersionText", result.StatusColorHex);

            if (!result.DirectoryExists)
            {
                SetText("StatusBarText", "状态：等待选择客户端目录");
                return;
            }

            if (result.IsRecommendedState)
            {
                SetText("StatusBarText", "状态：客户端状态良好");
            }
            else if (result.IsLaunchable)
            {
                SetText("StatusBarText", "状态：客户端可启动，但建议先修复");
            }
            else
            {
                SetText("StatusBarText", "状态：未检测到完整客户端");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"检测客户端异常：{ex.Message}");
        }
    }

    private async Task FixRealmlistAsync()
    {
        try
        {
            var clientPath = GetTextBoxText("ClientPathBox").Trim();
            if (!Directory.Exists(clientPath))
            {
                AppendLog("请先选择有效的客户端目录。");
                return;
            }

            var realmlistValue = $"set realmlist {_launcherConfig.RealmHost}";
            var targets = new[]
            {
                Path.Combine(clientPath, "realmlist.wtf"),
                Path.Combine(clientPath, "Data", "zhCN", "realmlist.wtf"),
                Path.Combine(clientPath, "Data", "zhTW", "realmlist.wtf"),
                Path.Combine(clientPath, "Data", "enUS", "realmlist.wtf")
            };

            var successCount = 0;
            foreach (var path in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    if (File.Exists(path))
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                    }

                    await File.WriteAllTextAsync(path, realmlistValue + Environment.NewLine);
                    successCount++;
                    AppendLog($"Realmlist 已写入：{path}");
                }
                catch (Exception ex)
                {
                    AppendLog($"Realmlist 写入失败：{path}，原因：{ex.Message}");
                }
            }

            SetText("RealmlistText", realmlistValue);
            AppendLog($"Realmlist 修复完成，成功写入 {successCount}/{targets.Length} 个位置。");
            InspectClientPath(clientPath);
        }
        catch (Exception ex)
        {
            AppendLog($"修复 Realmlist 失败：{ex.Message}");
        }
    }

    private async Task ClearCacheAsync()
    {
        try
        {
            var clientPath = GetTextBoxText("ClientPathBox").Trim();
            if (!Directory.Exists(clientPath))
            {
                AppendLog("请先选择有效的客户端目录。");
                return;
            }

            SetBusy(true, "状态：正在清理缓存...");

            await Task.Run(() =>
            {
                DeleteDirectorySafe(Path.Combine(clientPath, "Cache"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Data", "Cache"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Logs"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Errors"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Screenshots"));
            });

            AppendLog("缓存、日志、错误与截图目录已清理。");
            InspectClientPath(clientPath);
        }
        catch (Exception ex)
        {
            AppendLog($"清理缓存失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false, "状态：缓存清理完成");
        }
    }

    private async Task DeepCleanClientAsync()
    {
        try
        {
            var clientPath = GetTextBoxText("ClientPathBox").Trim();
            AppendLog($"修复客户端入口触发，当前选择目录：{clientPath}");
            if (!Directory.Exists(clientPath))
            {
                AppendLog("请先选择有效的客户端目录。");
                return;
            }

            var markerPath = Path.Combine(clientPath, "repair_button_clicked.txt");
            await File.WriteAllTextAsync(markerPath, $"Repair button clicked at {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
            AppendLog($"已写入修复按钮触发标记：{markerPath}");

            SetText("StatusBarText", "状态：已点击修复客户端，正在处理...");
            SetText("DownloadProgressText", "正在修复客户端...");
            SetText("DownloadSpeedText", "清理缓存/日志/错误目录");
            SetText("ServerStatusText", "修复中");
            SetForeground("ServerStatusText", "#D3A14A");
            SetProgress("RightProgressBar", 8);
            SetBusy(true, "状态：正在执行客户端修复...");
            AppendLog("按钮点击：修复客户端，开始执行深度修复...");

            await Task.Run(() =>
            {
                DeleteDirectorySafe(Path.Combine(clientPath, "Cache"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Data", "Cache"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Logs"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Errors"));
                DeleteDirectorySafe(Path.Combine(clientPath, "Screenshots"));
            });

            SetProgress("RightProgressBar", 55);
            SetText("DownloadProgressText", "基础缓存清理完成，正在执行修复脚本...");
            AppendLog("基础缓存清理完成，开始执行 repair_client.cmd ...");

            var result = await Task.Run(() => _repairServices.RunRepairScript(clientPath));
            AppendLog(result.Message);

            SetProgress("RightProgressBar", 85);
            SetText("DownloadProgressText", result.Success ? "修复脚本已启动/执行完成" : "修复脚本执行失败");
            SetText("DownloadSpeedText", result.Message);
            SetText("ServerStatusText", result.Success ? "完成" : "失败");
            SetForeground("ServerStatusText", result.Success ? "#67E06E" : "#FF6B6B");

            InspectClientPath(clientPath);
            await CheckUpdateAsync();
            SetProgress("RightProgressBar", 100);
        }
        catch (Exception ex)
        {
            AppendLog($"深度清理失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false, "状态：客户端修复流程结束");
        }
    }

    private async Task DownloadClientAsync()
    {
        try
        {
            SetBusy(true, "状态：正在下载客户端...");
            SetProgress("RightProgressBar", 0);
            SetText("DownloadProgressText", "准备下载...");
            SetText("DownloadSpeedText", "-");
            SetText("ServerStatusText", "下载中");

            AppendLog("开始下载客户端到桌面。");

            var result = await _downloadServices.DownloadClientAsync(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                progress =>
                {
                    SetProgress("RightProgressBar", progress.ProgressPercent);
                    SetText("DownloadProgressText", $"{progress.ProgressPercent:0.0}%  {progress.StatusText}");
                    SetText("DownloadSpeedText", progress.SpeedText);
                });

            var config = _downloadServices.LoadConfig();
            AppendLog($"下载地址：{config.DownloadUrl}");
            AppendLog($"输出文件：{config.OutputFileName}");
            AppendLog(result.Message);

            SetProgress("RightProgressBar", 100);
            SetText("DownloadProgressText", "下载完成");
            SetText("ServerStatusText", "完成");
            SetForeground("ServerStatusText", "#D3A14A");
        }
        catch (Exception ex)
        {
            AppendLog($"下载失败：{ex.Message}");
            SetText("DownloadProgressText", $"下载失败：{ex.Message}");
            SetText("ServerStatusText", "失败");
            SetForeground("ServerStatusText", "#FF6B6B");
        }
        finally
        {
            SetBusy(false, "状态：下载任务结束");
        }
    }

    private async Task OpenRegisterWindowAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_launcherConfig.RegisterUrl))
            {
                AppendLog("未配置注册网址。");
                return;
            }

            if (_registerWindow is not null)
            {
                if (_registerWindow.IsVisible)
                {
                    _registerWindow.WindowState = WindowState.Normal;
                    _registerWindow.Activate();
                    AppendLog("注册窗口已存在，已切换到前台。");
                    return;
                }

                _registerWindow = null;
            }

            _registerWindow = new RegisterWindow(_launcherConfig.RegisterUrl)
            {
                Icon = Icon,
                ShowInTaskbar = true,
                CanResize = false
            };

            _registerWindow.Closed += (_, _) => _registerWindow = null;

            AppendLog($"准备打开内置注册窗口：{_launcherConfig.RegisterUrl}");
            _registerWindow.Show();
            _registerWindow.Activate();
            AppendLog($"已打开内置注册窗口：{_launcherConfig.RegisterUrl}");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppendLog($"打开内置注册窗口失败：{ex.Message}");
        }
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                AppendLog("链接为空，无法打开。");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            AppendLog($"已打开链接：{url}");
        }
        catch (Exception ex)
        {
            AppendLog($"打开链接失败：{ex.Message}");
        }
    }

    private async Task EnterGameAsync()
    {
        try
        {
            var clientPath = GetTextBoxText("ClientPathBox").Trim();
            if (!Directory.Exists(clientPath))
            {
                AppendLog("请先选择有效的客户端目录。");
                return;
            }

            var wowExe = _clientInspectService.GetGameExecutable(clientPath);
            if (string.IsNullOrWhiteSpace(wowExe) || !File.Exists(wowExe))
            {
                AppendLog("未找到 Wow.exe 或 Wow-64.exe。");
                return;
            }

            AppendLog($"启动目标客户端目录：{clientPath}");
            AppendLog($"启动目标可执行文件：{wowExe}");
            SetText("DownloadProgressText", $"即将启动：{Path.GetFileName(wowExe)}");
            SetText("DownloadSpeedText", clientPath);

            SetBusy(true, "状态：正在启动游戏...");
            AppendLog("进入游戏前先执行正式服补丁检查...");
            await PatchUpdateAsync(forceDownload: false);

            // 验证补丁是否最新，不最新则禁止启动
            var isUpToDate = await _legacyUpdateService.IsPatchUpToDateAsync(clientPath, _useTestServer);
            if (!isUpToDate)
            {
                AppendLog("补丁未更新到最新版本，禁止启动游戏。请关闭游戏后重新点击检查更新。");
                SetText("DownloadProgressText", "⚠ 补丁未更新，请关闭游戏后点检查更新");
                SetText("DownloadSpeedText", "无法启动游戏");
                return;
            }

            // Ensure AIO + NolanUnid addons are installed (covers disk files, MPQ handles rest)
            await AddonService.EnsureAddonsAsync(clientPath, msg => AppendLog(msg));

            await FixRealmlistAsync();

            var rootRealmlist = Path.Combine(clientPath, "realmlist.wtf");
            var zhCnRealmlist = Path.Combine(clientPath, "Data", "zhCN", "realmlist.wtf");
            AppendLog($"根目录 realmlist：{(File.Exists(rootRealmlist) ? File.ReadAllText(rootRealmlist).Trim() : "不存在")}");
            AppendLog($"zhCN realmlist：{(File.Exists(zhCnRealmlist) ? File.ReadAllText(zhCnRealmlist).Trim() : "不存在")}");

            Process.Start(new ProcessStartInfo
            {
                FileName = wowExe,
                WorkingDirectory = clientPath,
                UseShellExecute = true
            });

            AppendLog($"已启动游戏：{Path.GetFileName(wowExe)}");
            SetText("StatusBarText", "状态：游戏已启动");
        }
        catch (Exception ex)
        {
            AppendLog($"启动游戏失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false, "状态：待命");
        }
    }

    private async Task PatchUpdateAsync(bool forceDownload = false)
    {
        try
        {
            var clientPath = GetTextBoxText("ClientPathBox").Trim();
            SetText("DownloadProgressText", "已点击检查更新，正在准备...");
            SetText("DownloadSpeedText", "正在初始化任务");
            SetText("ServerStatusText", "更新中");
            SetForeground("ServerStatusText", "#D3A14A");
            SetProgress("RightProgressBar", 2);

            if (!Directory.Exists(clientPath))
            {
                AppendLog("请先选择有效的客户端目录。");
                SetText("DownloadProgressText", "请先选择有效的客户端目录");
                SetText("DownloadSpeedText", "未开始");
                SetText("ServerStatusText", "未选择目录");
                SetForeground("ServerStatusText", "#FF6B6B");
                SetProgress("RightProgressBar", 0);
                return;
            }

            SetBusy(true, "状态：正在检查服务器补丁...");
            SetProgress("RightProgressBar", 5);

            AppendLog("LEGACY_UPDATE_CORE：已接入旧版正式服更新内核。");
            AppendLog("开始检查正式服 combined-release-version / combined-release-manifest...");

            var version = await _legacyUpdateService.GetVersionAsync(_useTestServer);
            var allPatchStatus = await _legacyUpdateService.GetPatchStatusAsync(clientPath, _useTestServer);
            foreach (var statusPatch in allPatchStatus)
                AppendLog($"补丁状态诊断：name={statusPatch.Name}, file={statusPatch.FileName}, relative={statusPatch.RelativePath}, state={statusPatch.State}, hash={statusPatch.Hash}");

            var missing = forceDownload
                ? await _legacyUpdateService.GetPatchesAsync(_useTestServer)
                : allPatchStatus.Where(p => p.State.StartsWith("缺失", StringComparison.OrdinalIgnoreCase) || p.State.StartsWith("可更新", StringComparison.OrdinalIgnoreCase) || p.State == "待校验").ToList();

            AppendLog($"补丁更新诊断：forceDownload={forceDownload}, missing.Count={missing.Count}, clientPath={clientPath}");
            AppendLog(forceDownload
                ? $"用户点击检查更新：按旧版逻辑强制拉取正式服补丁（{missing.Count} 个），目标版本 {version}。"
                : $"旧版逻辑检测到需要更新的补丁：{missing.Count} 个，目标版本 {version}。");

            if (missing.Count == 0)
            {
                AppendLog("所有补丁已是最新，无需更新。");
                SetText("ServerStatusText", "最新");
                SetForeground("ServerStatusText", "#67E06E");
                SetText("DownloadProgressText", "补丁已是最新");
                SetProgress("RightProgressBar", 100);
                return;
            }

            var dataDir = Path.Combine(clientPath, "Data", "zhCN");
            Directory.CreateDirectory(dataDir);

            var config = _downloadServices.LoadConfig();
            var patchBaseUrl = string.IsNullOrWhiteSpace(config.PatchBaseUrl)
                ? "http://43.248.129.172:88/patches-channels/"
                : config.PatchBaseUrl.Trim();

            if (!patchBaseUrl.EndsWith('/'))
                patchBaseUrl += "/";

            int completed = 0;

            foreach (var patch in missing)
            {
                if (string.IsNullOrWhiteSpace(patch.FileName))
                    continue;

                var patchUrl = !string.IsNullOrWhiteSpace(patch.DownloadUrl)
                    ? patch.DownloadUrl!
                    : patchBaseUrl + patch.FileName;
                AppendLog($"下载补丁：{patch.Name} ({patch.FileName})...");
                AppendLog($"补丁请求地址：{patchUrl}");

                try
                {
                    var targetPath = !string.IsNullOrWhiteSpace(patch.RelativePath)
                        ? Path.Combine(clientPath, patch.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar))
                        : Path.Combine(dataDir, patch.FileName);
                    AppendLog($"补丁保存路径：{targetPath}");
                    AppendLog($"下载前检查：exists={File.Exists(targetPath)}, size={(File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0)}");

                    var itemProgress = new Progress<(double Percent, string Status, string Detail)>(p =>
                    {
                        var overallPercent = (completed + p.Percent / 100.0) * 100.0 / missing.Count;
                        SetProgress("RightProgressBar", overallPercent);
                        SetText("DownloadProgressText", p.Status);
                        SetText("DownloadSpeedText", p.Detail);
                    });

                    AppendLog($"开始执行 DownloadPatchAsync：{patch.Name}");
                    await _legacyUpdateService.DownloadPatchAsync(patch, clientPath, itemProgress);
                    AppendLog($"DownloadPatchAsync 返回：exists={File.Exists(targetPath)}, size={(File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0)}");
                    if (File.Exists(targetPath))
                    {
                        var actualHash = await ComputeFileSha256Async(targetPath);
                        AppendLog($"下载后 hash：local={actualHash}, manifest={patch.Hash}");
                    }

                    completed++;
                    var percent = completed * 100.0 / missing.Count;
                    SetText("DownloadProgressText", $"补丁 {completed}/{missing.Count}：{patch.Name}");
                    SetText("DownloadSpeedText", "已下载并校验完成");
                    SetProgress("RightProgressBar", percent);
                    await Task.Delay(250);

                    AppendLog($"补丁已下载并校验：{patch.FileName}");
                }
                catch (Exception ex)
                {
                    AppendLog($"补丁下载失败 {patch.FileName}：{ex.Message}");
                    SetText("DownloadProgressText", $"⚠ 补丁更新失败：{patch.Name}");
                    SetText("DownloadSpeedText", ex.Message);
                    SetForeground("ServerStatusText", "#FF6B6B");
                    SetText("ServerStatusText", "更新失败");
                    SetProgress("RightProgressBar", 0);
                    patch.State = $"更新失败：{ex.Message}";
                    patch.StateColor = "#FF6B6B";
                }
            }

            SetProgress("RightProgressBar", 100);

            if (completed < missing.Count)
            {
                SetText("DownloadProgressText", $"⚠ 补丁更新不完整（{completed}/{missing.Count}），请关闭游戏后重试");
                SetText("DownloadSpeedText", "部分补丁未成功");
                SetText("ServerStatusText", "更新失败");
                SetForeground("ServerStatusText", "#FF6B6B");
                AppendLog($"补丁更新不完整（{completed}/{missing.Count}）。");
            }
            else
            {
                SetText("DownloadProgressText", $"补丁更新成功（{completed}/{missing.Count}）");
                SetText("DownloadSpeedText", "写入完成");
                AppendLog($"补丁更新完成（{completed}/{missing.Count}）。");
            }

            await Task.Delay(400);

            await CheckUpdateAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"补丁更新失败：{ex.Message}");
            SetText("ServerStatusText", "失败");
            SetForeground("ServerStatusText", "#FF6B6B");
            SetText("DownloadProgressText", $"补丁更新失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false, "状态：补丁更新结束");
        }
    }

    private async Task RefreshAnnouncementAsync()
    {
        try
        {
            var announcement = await _announcementService.LoadAsync(
                _launcherConfig.AnnouncementUrl,
                _launcherConfig);

            SetText("AnnouncementUpdatedText", $"最近更新：{announcement.UpdatedAt}");
            SetText("AnnouncementTitleText", announcement.Title);
            SetText("AnnouncementBodyText", announcement.Body);
            SetText("AnnouncementLine1", announcement.Line1);
            SetText("AnnouncementLine2", announcement.Line2);
            SetText("AnnouncementLine3", announcement.Line3);
            SetText("AnnouncementLine4", announcement.Line4);

            AppendLog("公告已刷新。");
        }
        catch (Exception ex)
        {
            AppendLog($"刷新公告失败：{ex.Message}");
        }
    }

    private async Task CheckUpdateAsync()
    {
        try
        {
            SetBusy(true, "状态：正在检查补丁更新...");
            await Task.Delay(150);

            var clientPath = GetTextBoxText("ClientPathBox").Trim();
            var items = Directory.Exists(clientPath)
                ? await _legacyUpdateService.GetPatchStatusAsync(clientPath, _useTestServer)
                : _patchService.LoadPatchItems(clientPath).ToList();
            RenderPatchList(items);
            SetText("PatchCountText", $"补丁列表（{items.Count}）");
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.RelativePath))
                    continue;

                var fullPath = Path.Combine(clientPath, item.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                var exists = File.Exists(fullPath);
                var size = exists ? new FileInfo(fullPath).Length : 0;
                AppendLog($"补丁诊断：{item.Name} | 状态={item.State} | 目标路径={fullPath} | exists={exists} | size={size} | hash={item.Hash}");
            }
            AppendLog("补丁检查完成：列表状态已按正式服 manifest 的 LocalRelativePath + SHA256 刷新。");
        }
        catch (Exception ex)
        {
            AppendLog($"检查更新失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false, "状态：补丁检查完成");
        }
    }

    private void RenderPatchList(IEnumerable<PatchItem> items)
    {
        var panel = FindCtrl<StackPanel>("PatchListPanel");
        if (panel is null)
        {
            AppendLog("未找到控件：PatchListPanel");
            return;
        }

        panel.Children.Clear();

        foreach (var item in items)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#80613F18")),
                Background = new SolidColorBrush(Color.Parse("#B10A1018")),
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("2.5*,1.6*,1.2*,1.15*,0.95*")
            };

            var nameText = new TextBlock
            {
                Text = item.Name,
                Foreground = new SolidColorBrush(Color.Parse("#F1E3CC")),
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(nameText);

            var versionText = new TextBlock
            {
                Text = item.Version,
                Foreground = new SolidColorBrush(Color.Parse("#E2B45A")),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(versionText, 1);
            grid.Children.Add(versionText);

            var sizeText = new TextBlock
            {
                Text = item.Size,
                Foreground = new SolidColorBrush(Color.Parse("#C5B08B")),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetColumn(sizeText, 2);
            grid.Children.Add(sizeText);

            var stateBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#28000000")),
                BorderBrush = new SolidColorBrush(Color.Parse(item.StateColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = item.State,
                    Foreground = new SolidColorBrush(Color.Parse(item.StateColor)),
                    FontSize = 14,
                    FontWeight = FontWeight.Bold
                }
            };
            Grid.SetColumn(stateBorder, 3);
            grid.Children.Add(stateBorder);

            var action = string.IsNullOrWhiteSpace(item.FileName)
                ? "已安装"
                : item.State is "缺失" or "可更新" or "待校验"
                    ? "安装"
                    : "已安装";

            var actionColor = action == "安装" ? "#F0C97E" : "#67E06E";

            var actionText = new TextBlock
            {
                Text = action,
                Foreground = new SolidColorBrush(Color.Parse(actionColor)),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(actionText, 4);
            grid.Children.Add(actionText);

            border.Child = grid;
            panel.Children.Add(border);
        }
    }

    private void WireWoWButtonEffects()
    {
        foreach (var name in GetInteractiveButtonNames())
        {
            var button = FindCtrl<Button>(name);
            if (button is null)
            {
                AppendLog($"未找到按钮：{name}");
                continue;
            }

            WireButtonVisuals(button);
            WireButtonSounds(button);
        }
    }

    private IEnumerable<string> GetInteractiveButtonNames()
    {
        return new[]
        {
            "BrowseClientButton",
            "FixRealmlistButton",
            "ClearCacheButton",
            "RepairClientFullButton",
            "EnterGameButton",
            "RegisterButton",
            "CheckUpdateButton",
            "PatchUpdateButton",
            "DownloadClientButton",
            "WoWDatabaseButton",
            "ExitGameButton",
            "RefreshAnnouncementButton",
            "MinimizeButton",
            "ExitLauncherButton"
        };
    }

    private void WireButtonVisuals(Button button)
    {
        if (_buttonVisualStates.ContainsKey(button))
            return;

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);

        button.RenderTransformOrigin = RelativePoint.Center;
        button.RenderTransform = group;

        var isMainButton = button.Name == "EnterGameButton";

        _buttonVisualStates[button] = new ButtonVisualState(
            scale,
            translate,
            button.Opacity,
            isMainButton);

        button.PointerEntered += OnButtonPointerEntered;
        button.PointerExited += OnButtonPointerExited;
        button.PointerPressed += OnButtonPointerPressed;
        button.PointerReleased += OnButtonPointerReleased;
        button.LostFocus += OnButtonLostFocus;
    }

    private void WireButtonSounds(Button button)
    {
        if (!_wiredSoundButtons.Add(button))
            return;

        button.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
                _ = PlayUiSoundAsync("button_down.wav");
        };
        button.Click += (_, _) => _ = PlayUiSoundAsync("button_up.wav");
    }

    private void OnButtonPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button button && _buttonVisualStates.TryGetValue(button, out var state))
            ApplyButtonVisualState(button, state, hovered: true, pressed: false);
    }

    private void OnButtonPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button button && _buttonVisualStates.TryGetValue(button, out var state))
            ApplyButtonVisualState(button, state, hovered: false, pressed: false);
    }

    private void OnButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button button && _buttonVisualStates.TryGetValue(button, out var state))
            ApplyButtonVisualState(button, state, hovered: true, pressed: true);
    }

    private void OnButtonPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Button button && _buttonVisualStates.TryGetValue(button, out var state))
        {
            var point = e.GetPosition(button);
            var isInside = point.X >= 0 && point.Y >= 0 && point.X <= button.Bounds.Width && point.Y <= button.Bounds.Height;
            ApplyButtonVisualState(button, state, hovered: isInside, pressed: false);
        }
    }

    private void OnButtonLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && _buttonVisualStates.TryGetValue(button, out var state))
            ApplyButtonVisualState(button, state, hovered: false, pressed: false);
    }

    private void ApplyButtonVisualState(Button button, ButtonVisualState state, bool hovered, bool pressed)
    {
        if (!button.IsEnabled)
        {
            state.Scale.ScaleX = 1.0;
            state.Scale.ScaleY = 1.0;
            state.Translate.X = 0.0;
            state.Translate.Y = 0.0;
            button.Opacity = 0.72;
            return;
        }

        if (pressed)
        {
            if (state.IsMainButton)
            {
                state.Scale.ScaleX = 0.978;
                state.Scale.ScaleY = 0.978;
                state.Translate.Y = 2.8;
            }
            else
            {
                state.Scale.ScaleX = 0.988;
                state.Scale.ScaleY = 0.988;
                state.Translate.Y = 1.6;
            }

            state.Translate.X = 0.0;
            button.Opacity = 1.0;
            return;
        }

        if (hovered)
        {
            if (state.IsMainButton)
            {
                state.Scale.ScaleX = 1.018;
                state.Scale.ScaleY = 1.018;
                state.Translate.Y = -2.0;
            }
            else
            {
                state.Scale.ScaleX = 1.012;
                state.Scale.ScaleY = 1.012;
                state.Translate.Y = -1.1;
            }

            state.Translate.X = 0.0;
            button.Opacity = 1.0;
            return;
        }

        state.Scale.ScaleX = 1.0;
        state.Scale.ScaleY = 1.0;
        state.Translate.X = 0.0;
        state.Translate.Y = 0.0;
        button.Opacity = 1.0;
    }

    private async Task PlayUiSoundAsync(string fileName)
    {
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    using var stream = EmbeddedResources.OpenAsset($"Assets/Sounds/{fileName}");
                    if (stream is null)
                        return;

                    if (OperatingSystem.IsWindows())
                    {
                        using var player = new System.Media.SoundPlayer(stream);
                        player.PlaySync();
                    }
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }

    private static void DeleteDirectorySafe(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private void AppendLog(string message)
    {
        var logBox = FindCtrl<TextBox>("LogBox");
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        if (logBox is not null)
        {
            if (!string.IsNullOrWhiteSpace(logBox.Text))
            {
                var lines = logBox.Text.Split(Environment.NewLine);
                if (lines.Length > 200)
                    logBox.Text = string.Join(Environment.NewLine, lines.Skip(lines.Length - 180));
            }

            logBox.Text = string.IsNullOrWhiteSpace(logBox.Text)
                ? line
                : $"{logBox.Text}{Environment.NewLine}{line}";

            logBox.CaretIndex = logBox.Text?.Length ?? 0;
        }

        var fileLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}";
        SafeAppendFileLog(fileLine);
        SafeAppendClientFileLog(fileLine);
    }

    private void SafeAppendClientFileLog(string text)
    {
        try
        {
            var clientPath = GetTextBoxText("ClientPathBox").Trim();
            if (string.IsNullOrWhiteSpace(clientPath) || !Directory.Exists(clientPath))
                return;

            var logPath = Path.Combine(clientPath, "NolanLauncher_debug.log");
            File.AppendAllText(logPath, text);
        }
        catch
        {
        }
    }

    private void SetBusy(bool isBusy, string statusText)
    {
        SetButtonEnabled("BrowseClientButton", !isBusy);
        SetButtonEnabled("FixRealmlistButton", !isBusy);
        SetButtonEnabled("ClearCacheButton", !isBusy);
        SetButtonEnabled("RepairClientFullButton", !isBusy);
        SetButtonEnabled("RegisterButton", !isBusy);
        SetButtonEnabled("CheckUpdateButton", !isBusy);
        SetButtonEnabled("PatchUpdateButton", !isBusy);
        SetButtonEnabled("RefreshAnnouncementButton", !isBusy);
        SetButtonEnabled("DownloadClientButton", !isBusy);

        SetText("StatusBarText", statusText);
    }

    private void LoadHeroImage()
    {
        var images = new (string Name, string RelPath)[]
        {
            ("HeroImage", "Assets/hero/hero_main.png"),
            ("LogoImage", "Assets/logo/logo.png"),
            ("BannerRaidImage", "Assets/banners/banner_raid.png"),
            ("BannerEventImage", "Assets/banners/banner_event.png"),
            ("BannerFeatureImage", "Assets/banners/banner_feature.png"),
        };

        foreach (var (name, rel) in images)
        {
            try
            {
                var ctrl = FindCtrl<Image>(name);
                if (ctrl is null)
                    continue;

                using var fs = EmbeddedResources.OpenAsset(rel);
                if (fs is null)
                    continue;

                ctrl.Source = new Bitmap(fs);
            }
            catch
            {
            }
        }
    }

    private void TryLoadBackground()
    {
        var backgroundImage = FindCtrl<Image>("BackgroundImage");
        if (backgroundImage is null)
        {
            AppendLog("未找到控件：BackgroundImage");
            return;
        }

        string[] candidates =
        {
            "Assets/bg/main_bg.jpg",
            "Assets/bg.jpg",
            "Assets/bg.jpeg",
            "Assets/bg.png",
            "新登陆器背景.png"
        };

        foreach (var rel in candidates)
        {
            try
            {
                using var fs = EmbeddedResources.OpenAsset(rel);
                if (fs is null)
                    continue;

                backgroundImage.Source = new Bitmap(fs);
                AppendLog($"背景图已加载：{Path.GetFileName(rel)}");
                return;
            }
            catch (Exception ex)
            {
                AppendLog($"背景图加载失败：{ex.Message}");
            }
        }

        AppendLog("未找到可用背景图。");
    }

    private T? FindCtrl<T>(string name) where T : Avalonia.Controls.Control
    {
        return this.FindControl<T>(name);
    }

    private void SetText(string controlName, string? value)
    {
        var tb = FindCtrl<TextBlock>(controlName);
        if (tb is not null)
        {
            tb.Text = value ?? string.Empty;
            return;
        }

        var txt = FindCtrl<TextBox>(controlName);
        if (txt is not null)
        {
            txt.Text = value ?? string.Empty;
        }
    }

    private string GetTextBoxText(string controlName)
    {
        return FindCtrl<TextBox>(controlName)?.Text ?? string.Empty;
    }

    private void SetTextBoxText(string controlName, string? value)
    {
        var txt = FindCtrl<TextBox>(controlName);
        if (txt is not null)
            txt.Text = value ?? string.Empty;
    }

    private void SetForeground(string controlName, string colorHex)
    {
        var brush = new SolidColorBrush(Color.Parse(colorHex));

        var tb = FindCtrl<TextBlock>(controlName);
        if (tb is not null)
            tb.Foreground = brush;
    }

    private void SetButtonEnabled(string controlName, bool enabled)
    {
        var btn = FindCtrl<Button>(controlName);
        if (btn is not null)
        {
            btn.IsEnabled = enabled;

            if (_buttonVisualStates.TryGetValue(btn, out var state))
                ApplyButtonVisualState(btn, state, hovered: false, pressed: false);
        }
    }

    private void SetProgress(string controlName, double value)
    {
        var bar = FindCtrl<ProgressBar>(controlName);
        if (bar is not null)
            bar.Value = value;
    }

    private void StartEnterGameParticles()
    {
        if (FindCtrl<Canvas>("EnterGameParticlesCanvas") is not { } canvas)
        {
            AppendLog("未找到控件：EnterGameParticlesCanvas");
            return;
        }

        void SpawnParticle()
        {
            var dot = new Ellipse
            {
                Width = _particleRandom.Next(2, 5),
                Height = _particleRandom.Next(2, 5),
                Fill = new SolidColorBrush(
                    Color.Parse(_particleRandom.Next(0, 2) == 0 ? "#FFF0B8" : "#FFD86E")),
                Opacity = 0.0,
                IsHitTestVisible = false
            };

            var startX = _particleRandom.NextDouble() * 320 + 12;
            var startY = 88 + _particleRandom.NextDouble() * 8;

            Canvas.SetLeft(dot, startX);
            Canvas.SetTop(dot, startY);
            canvas.Children.Add(dot);

            var life = 0;
            var maxLife = 24 + _particleRandom.Next(0, 10);
            var rise = 12 + _particleRandom.NextDouble() * 16;
            var drift = (_particleRandom.NextDouble() - 0.5) * 12;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                life++;
                var t = (double)life / maxLife;
                var ease = t < 0.5 ? t * 2 : (1.0 - t) * 2;

                dot.Opacity = 0.55 * Math.Max(0, ease);
                Canvas.SetLeft(dot, startX + drift * t);
                Canvas.SetTop(dot, startY - rise * t);

                if (life >= maxLife)
                {
                    timer.Stop();
                    canvas.Children.Remove(dot);
                }
            };
            timer.Start();
        }

        _enterGameParticleTimer?.Stop();
        _enterGameParticleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _enterGameParticleTimer.Tick += (_, _) =>
        {
            if (!IsVisible || !canvas.IsEffectivelyVisible)
                return;

            SpawnParticle();
            if (_particleRandom.NextDouble() < 0.55)
                SpawnParticle();
        };
        _enterGameParticleTimer.Start();
    }

    private void SafeAppendFileLog(string text)
    {
        try
        {
            File.AppendAllText(LogPath, text);
        }
        catch
        {
        }
    }

    private string GetDatabaseUrl()
    {
        var prop = _launcherConfig.GetType().GetProperty("WoWDatabaseUrl");
        var value = prop?.GetValue(_launcherConfig) as string;
        return string.IsNullOrWhiteSpace(value) ? "https://80.nfuwow.com/" : value;
    }

    private static async Task<string> ComputeFileSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Source is Button || e.Source is TextBox)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private sealed class ButtonVisualState
    {
        public ButtonVisualState(ScaleTransform scale, TranslateTransform translate, double baseOpacity, bool isMainButton)
        {
            Scale = scale;
            Translate = translate;
            BaseOpacity = baseOpacity;
            IsMainButton = isMainButton;
        }

        public ScaleTransform Scale { get; }
        public TranslateTransform Translate { get; }
        public double BaseOpacity { get; }
        public bool IsMainButton { get; }
    }
}
