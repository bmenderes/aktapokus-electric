using System.Text;
using Serilog;
using Serilog.Events;

namespace Ake.Cli;

internal static class Program
{
    private const int ExitUnexpected = 2;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Log.Logger = CreateLogger();
        try
        {
            var userLibrary = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "aktapokus-electric",
                "library.db");
            return CliApp.Run(args, Console.Out, Console.Error, userLibrary);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Beklenmeyen hata");
            return ExitUnexpected;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static Serilog.Core.Logger CreateLogger()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "aktapokus-electric",
            "logs");

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Warning,
                standardErrorFromLevel: LogEventLevel.Verbose)
            .WriteTo.File(
                Path.Combine(logDirectory, "ake-cli-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }
}
