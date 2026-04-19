using System;
using System.Threading.Tasks;

namespace NolanWoWLauncher.Services;

public sealed class AddonService
{
    public static async Task<bool> EnsureAddonsAsync(string clientPath, Action<string>? log = null)
    {
        // Addons are served from MPQ patch. No disk extraction needed.
        log?.Invoke("[插件] 使用MPQ内置插件");
        await Task.CompletedTask;
        return true;
    }
}
