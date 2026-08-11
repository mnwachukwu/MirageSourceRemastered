using Avalonia;
using Velopack;

namespace Mirage.Editor;

/// <summary>Process entry point. Runs the Velopack update hooks before Avalonia starts, since an
/// install or update step may need to exit the process before any UI exists.</summary>
sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia host configuration. Also called by the XAML previewer, which requires this
    /// exact name and signature.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
