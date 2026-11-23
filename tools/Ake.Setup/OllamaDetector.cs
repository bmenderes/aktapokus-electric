using Microsoft.Win32;

namespace Ake.Setup;

/// <summary>Bilgisayarda mevcut Ollama kurulumunu tespit eder.</summary>
public sealed record OllamaStatus(bool IsInstalled, string? ExecutablePath);

public static class OllamaDetector
{
    public static OllamaStatus Detect()
    {
        var candidates = new List<string>();
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApp))
        {
            candidates.Add(Path.Combine(localApp, "Programs", "Ollama", "ollama.exe"));
        }

        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(progFiles))
        {
            candidates.Add(Path.Combine(progFiles, "Ollama", "ollama.exe"));
        }

        var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(progFilesX86))
        {
            candidates.Add(Path.Combine(progFilesX86, "Ollama", "ollama.exe"));
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(dir, "ollama.exe"));
                }
                catch (ArgumentException)
                {
                    // Geçersiz PATH girdisini yoksay.
                }
            }
        }

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return new OllamaStatus(true, path);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var fromRegistry = FindInRegistry();
            if (fromRegistry is not null)
            {
                return new OllamaStatus(true, fromRegistry);
            }
        }

        return new OllamaStatus(false, null);
    }

    private static string? FindInRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var roots = new[] { Registry.CurrentUser, Registry.LocalMachine };
        var subKeys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (var root in roots)
        {
            foreach (var subKeyPath in subKeys)
            {
                using var key = root.OpenSubKey(subKeyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var name in key.GetSubKeyNames())
                {
                    using var appKey = key.OpenSubKey(name);
                    var displayName = appKey?.GetValue("DisplayName") as string;
                    if (displayName is null || !displayName.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var installLocation = appKey?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                    {
                        var exe = Path.Combine(installLocation.Trim('"'), "ollama.exe");
                        if (File.Exists(exe))
                        {
                            return exe;
                        }
                    }

                    return displayName;
                }
            }
        }

        return null;
    }
}
