using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace NolanWoWLauncher;

public partial class App : Application
{
    private string LogPath => Path.Combine(EmbeddedResources.AppDataDir, "startup.log");

    public override void Initialize()
    {
        try
        {
            File.AppendAllText(LogPath, $"{Now()} | App.Initialize\n");
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception ex)
        {
            File.AppendAllText(LogPath, $"{Now()} | App.Initialize EXCEPTION\n{ex}\n");
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            File.AppendAllText(LogPath, $"{Now()} | Framework Init Start\n");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                File.AppendAllText(LogPath, $"{Now()} | Creating MainWindow\n");
                desktop.MainWindow = new MainWindow();
            }
            File.AppendAllText(LogPath, $"{Now()} | Framework Init End\n");
            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            File.AppendAllText(LogPath, $"{Now()} | Framework Init EXCEPTION\n{ex}\n");
            throw;
        }
    }

    private string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}
