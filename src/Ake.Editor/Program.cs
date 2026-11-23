using Avalonia;
using Serilog;

namespace Ake.Editor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Log.Logger = CreateLogger();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Yakalanmamış hata");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Gözlemlenmeyen görev hatası");
            e.SetObserved();
        };

        try
        {
            Log.Information("aktapokus-electric başlatılıyor");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Editör beklenmeyen hatayla kapandı");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();

    private static Serilog.Core.Logger CreateLogger()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "aktapokus-electric",
            "logs");

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "ake-editor-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }
}
