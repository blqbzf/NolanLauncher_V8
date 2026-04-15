using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia.Platform;

namespace NolanWoWLauncher;

internal static class EmbeddedResources
{
    public static string AppDataDir
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(baseDir, "NolanWoWLauncher");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ReadTextOrNull(string relativePath)
    {
        var diskPath = Path.Combine(AppDataDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(diskPath))
            return File.ReadAllText(diskPath, Encoding.UTF8);

        var uri = BuildAvaresUri(relativePath);
        if (AssetLoader.Exists(uri))
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return null;
    }

    public static Stream? OpenAsset(string relativePath)
    {
        var diskPath = Path.Combine(AppDataDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(diskPath))
            return File.OpenRead(diskPath);

        var uri = BuildAvaresUri(relativePath);
        return AssetLoader.Exists(uri) ? AssetLoader.Open(uri) : null;
    }

    public static string EnsureFileExtracted(string relativePath)
    {
        var targetPath = Path.Combine(AppDataDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDir))
            Directory.CreateDirectory(targetDir);

        if (!File.Exists(targetPath))
        {
            using var stream = OpenAsset(relativePath) ?? throw new FileNotFoundException($"内嵌资源不存在：{relativePath}");
            using var fs = File.Create(targetPath);
            stream.CopyTo(fs);
        }

        return targetPath;
    }

    private static Uri BuildAvaresUri(string relativePath)
        => new($"avares://NolanWoWLauncher/{relativePath.Replace('\\', '/')}");
}
