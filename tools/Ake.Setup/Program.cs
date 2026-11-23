using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Ake.Setup;

public sealed class SetupApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SetupWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Array.Exists(args, a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
        {
            var dir = InstallEngine.DefaultInstallDirectory;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--dir", StringComparison.OrdinalIgnoreCase))
                {
                    dir = args[i + 1];
                }
            }

            var noOllama = Array.Exists(args, a => a.Equals("--no-ollama", StringComparison.OrdinalIgnoreCase));
            var noDesktop = Array.Exists(args, a => a.Equals("--no-desktop", StringComparison.OrdinalIgnoreCase));
            var res = InstallEngine.InstallAsync(new InstallOptions(dir, !noDesktop, true, true, !noOllama))
                .GetAwaiter()
                .GetResult();
            return res.Success ? 0 : 1;
        }

        return AppBuilder.Configure<SetupApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args);
    }
}
