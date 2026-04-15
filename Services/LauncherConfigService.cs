using System.IO;
using System.Text;
using System.Text.Json;
using NolanWoWLauncher.Models;

namespace NolanWoWLauncher.Services;

public sealed class LauncherConfigService
{
    private readonly string _configPath = Path.Combine(EmbeddedResources.AppDataDir, "launcher_config.json");

    public LauncherConfig Load()
    {
        try
        {
            string? json;

            if (File.Exists(_configPath))
            {
                json = File.ReadAllText(_configPath, Encoding.UTF8);
            }
            else
            {
                json = EmbeddedResources.ReadTextOrNull("launcher_config.json");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                    File.WriteAllText(_configPath, json, Encoding.UTF8);
                }
            }

            return string.IsNullOrWhiteSpace(json)
                ? new LauncherConfig()
                : JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
        }
        catch
        {
            return new LauncherConfig();
        }
    }

    public void Save(LauncherConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, json, Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }
}
