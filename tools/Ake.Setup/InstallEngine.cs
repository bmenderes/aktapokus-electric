using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Microsoft.Win32;

namespace Ake.Setup;

public sealed record InstallOptions(
    string InstallDir,
    bool CreateDesktopShortcut,
    bool CreateStartMenuShortcut,
    bool AddToPath,
    bool InstallOllama);

public sealed record InstallResult(
    bool Success,
    string EditorExePath,
    string OllamaOutcome,
    string? ErrorMessage = null);

public static class InstallEngine
{
    private const string AppResourceName = "Ake.Setup.app.zip";
    private const string OllamaResourceName = "Ake.Setup.OllamaSetup.exe";
    private const string OllamaDownloadUrl = "https://ollama.com/download/OllamaSetup.exe";
    private const int MaxPayloadEntries = 64;
    private const long MaxPayloadBytes = 512L * 1024 * 1024;
    private const long MaxOllamaDownloadBytes = 1_500_000_000L;

    private static readonly char[] ForbiddenShellChars = ['"', '&', '|', '<', '>', '^', '%', '!', ';', '`', '\r', '\n'];

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "aktapokus-electric");

    public static bool HasEmbeddedPayload =>
        typeof(InstallEngine).Assembly.GetManifestResourceInfo(AppResourceName) is not null;

    public static bool HasEmbeddedOllama =>
        typeof(InstallEngine).Assembly.GetManifestResourceInfo(OllamaResourceName) is not null;

    /// <summary>Kurulum yolunun güvenli ve geçerli olup olmadığını denetler; hata varsa Türkçe neden döner.</summary>
    public static string? ValidateInstallDirectory(string? rawDir)
    {
        if (string.IsNullOrWhiteSpace(rawDir))
        {
            return "Kurulum klasörü boş olamaz.";
        }

        var trimmed = rawDir.Trim();
        if (HasForbiddenShellChars(trimmed))
        {
            return "Kurulum klasörü geçersiz veya güvensiz karakterler içeriyor.";
        }

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || !Path.IsPathRooted(trimmed))
        {
            return "Geçersiz kurulum klasörü: ağ (UNC) yolu veya göreli yol kullanılamaz.";
        }

        string normalized;
        try
        {
            normalized = TrimTrailingSeparators(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Geçersiz kurulum klasörü: {ex.Message}";
        }

        var root = TrimTrailingSeparators(Path.GetPathRoot(normalized) ?? string.Empty);
        if (string.IsNullOrEmpty(root) || string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
        {
            return "Doğrudan sürücü köküne kurulum yapılamaz; bir alt klasör seçin.";
        }

        var winDir = GetSpecialFolderNormalized(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(winDir) && IsSameOrSubpath(normalized, winDir))
        {
            return "Windows sistem klasörüne kurulum yapılamaz.";
        }

        var sysDir = GetSpecialFolderNormalized(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(sysDir) && IsSameOrSubpath(normalized, sysDir))
        {
            return "Sistem klasörüne kurulum yapılamaz.";
        }

        var sysX86Dir = GetSpecialFolderNormalized(Environment.SpecialFolder.SystemX86);
        if (!string.IsNullOrEmpty(sysX86Dir) && IsSameOrSubpath(normalized, sysX86Dir))
        {
            return "Sistem klasörüne kurulum yapılamaz.";
        }

        var localPrograms = Path.Combine(GetSpecialFolderNormalized(Environment.SpecialFolder.LocalApplicationData), "Programs");
        string[] directBlocked =
        [
            GetSpecialFolderNormalized(Environment.SpecialFolder.ProgramFiles),
            GetSpecialFolderNormalized(Environment.SpecialFolder.ProgramFilesX86),
            GetSpecialFolderNormalized(Environment.SpecialFolder.UserProfile),
            GetSpecialFolderNormalized(Environment.SpecialFolder.DesktopDirectory),
            GetSpecialFolderNormalized(Environment.SpecialFolder.MyDocuments),
            GetSpecialFolderNormalized(Environment.SpecialFolder.LocalApplicationData),
            GetSpecialFolderNormalized(Environment.SpecialFolder.ApplicationData),
            GetSpecialFolderNormalized(Environment.SpecialFolder.Programs),
            TrimTrailingSeparators(localPrograms),
        ];

        foreach (var blocked in directBlocked)
        {
            if (!string.IsNullOrEmpty(blocked) && string.Equals(normalized, blocked, StringComparison.OrdinalIgnoreCase))
            {
                return "Lütfen aktapokus-electric için ayrı bir alt klasör seçin (örn. ...\\aktapokus-electric).";
            }
        }

        return null;
    }

    public static async Task<InstallResult> InstallAsync(
        InstallOptions options,
        IProgress<(int Percent, string Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (ValidateInstallDirectory(options.InstallDir) is { } validationError)
        {
            return new InstallResult(false, string.Empty, "Hata", validationError);
        }

        var installDir = TrimTrailingSeparators(Path.GetFullPath(options.InstallDir.Trim()));
        var editorExe = Path.Combine(installDir, "Ake.Editor.exe");
        var cliExe = Path.Combine(installDir, "ake.exe");

        try
        {
            if (IsEditorRunningOrLocked(editorExe, cliExe))
            {
                return new InstallResult(
                    false,
                    editorExe,
                    "Beklemede",
                    "aktapokus-electric şu anda çalışıyor. Lütfen açık uygulamayı kapatıp 'Tekrar Dene' düğmesine tıklayın.");
            }

            progress?.Report((5, "Kurulum klasörü hazırlanıyor…"));
            Directory.CreateDirectory(installDir);

            var installedFiles = await ExtractAppPayloadAsync(installDir, progress, cancellationToken);

            progress?.Report((55, "Kaldırma kaydı ve ortam ayarları yazılıyor…"));
            WriteUninstaller(installDir, editorExe, installedFiles);

            if (options.AddToPath)
            {
                AddDirectoryToUserPath(installDir);
            }

            if (options.CreateStartMenuShortcut)
            {
                progress?.Report((62, "Başlat Menüsü kısayolu oluşturuluyor…"));
                var startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft",
                    "Windows",
                    "Start Menu",
                    "Programs");
                ShortcutCreator.Create(
                    Path.Combine(startMenuDir, "aktapokus-electric.lnk"),
                    editorExe,
                    installDir,
                    "aktapokus-electric Elektrik Projelendirme");
            }

            if (options.CreateDesktopShortcut)
            {
                progress?.Report((68, "Masaüstü kısayolu oluşturuluyor…"));
                var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                ShortcutCreator.Create(
                    Path.Combine(desktopDir, "aktapokus-electric.lnk"),
                    editorExe,
                    installDir,
                    "aktapokus-electric Elektrik Projelendirme");
            }

            var ollamaOutcome = await HandleOllamaAsync(options.InstallOllama, progress, cancellationToken);

            progress?.Report((100, "Kurulum tamamlandı."));
            return new InstallResult(true, editorExe, ollamaOutcome);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, editorExe, "İptal edildi", "Kurulum kullanıcı tarafından iptal edildi.");
        }
        catch (Exception ex)
        {
            return new InstallResult(false, editorExe, "Hata", ex.Message);
        }
    }

    private static bool IsEditorRunningOrLocked(string editorExe, string cliExe)
    {
        if (OperatingSystem.IsWindows())
        {
            var procs = Process.GetProcessesByName("Ake.Editor");
            try
            {
                if (procs.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var p in procs)
                {
                    p.Dispose();
                }
            }
        }

        return IsFileLocked(editorExe) || IsFileLocked(cliExe);
    }

    private static bool IsFileLocked(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static async Task<List<string>> ExtractAppPayloadAsync(
        string installDir,
        IProgress<(int Percent, string Message)>? progress,
        CancellationToken cancellationToken)
    {
        using var resourceStream = typeof(InstallEngine).Assembly.GetManifestResourceStream(AppResourceName);
        if (resourceStream is null)
        {
            throw new InvalidOperationException("Gömülü uygulama paketi (app.zip) bulunamadı. Önce build-setup.ps1 çalıştırın.");
        }

        using var archive = new ZipArchive(resourceStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count > MaxPayloadEntries)
        {
            throw new InvalidDataException($"Güvenlik sınırı aşıldı: arşivde en fazla {MaxPayloadEntries} dosya olabilir.");
        }

        var fullRoot = TrimTrailingSeparators(Path.GetFullPath(installDir)) + Path.DirectorySeparatorChar;
        var extractedRelativePaths = new List<string>(entries.Count);
        long totalWritten = 0;
        var buffer = new byte[81920];

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            if (Path.IsPathRooted(entry.FullName))
            {
                throw new InvalidDataException($"Güvensiz arşiv yolu: {entry.FullName}");
            }

            var destination = Path.GetFullPath(Path.Combine(installDir, entry.FullName));
            if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Güvensiz arşiv yolu: {entry.FullName}");
            }

            var relative = Path.GetRelativePath(fullRoot, destination);
            if (HasForbiddenShellChars(relative))
            {
                throw new InvalidDataException($"Güvensiz dosya adı: {relative}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var pct = 10 + (int)(42.0 * (i + 1) / Math.Max(1, entries.Count));
            progress?.Report((pct, $"Kopyalanıyor: {entry.Name}"));

            await using (var src = entry.Open())
            await using (var dst = File.Create(destination))
            {
                int read;
                while ((read = await src.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalWritten += read;
                    if (totalWritten > MaxPayloadBytes)
                    {
                        throw new InvalidDataException("Güvenlik sınırı aşıldı: arşiv toplam boyutu 512 MB sınırını geçti.");
                    }

                    await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            extractedRelativePaths.Add(relative);
        }

        return extractedRelativePaths;
    }

    private static async Task<string> HandleOllamaAsync(
        bool installRequested,
        IProgress<(int Percent, string Message)>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report((74, "Ollama kurulumu kontrol ediliyor…"));
        var status = OllamaDetector.Detect();
        if (status.IsInstalled)
        {
            progress?.Report((95, $"Ollama zaten kurulu ({status.ExecutablePath}); atlandı."));
            return $"Atlandı (zaten kurulu: {status.ExecutablePath})";
        }

        if (!installRequested)
        {
            progress?.Report((95, "Ollama kurulumu kullanıcı seçimiyle atlandı."));
            return "Atlandı (isteğe bağlı seçilmedi)";
        }

        var tempDir = Directory.CreateTempSubdirectory("ake-setup-");
        var installerPath = Path.Combine(tempDir.FullName, "OllamaSetup.exe");

        try
        {
            using var embeddedOllama = typeof(InstallEngine).Assembly.GetManifestResourceStream(OllamaResourceName);

            if (embeddedOllama is not null)
            {
                progress?.Report((78, "Gömülü Ollama kurulum paketi hazırlanıyor…"));
                await using var dst = File.Create(installerPath);
                await embeddedOllama.CopyToAsync(dst, cancellationToken);
            }
            else
            {
                progress?.Report((78, "OllamaSetup.exe indiriliyor (ollama.com)…"));
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                using var response = await http.GetAsync(OllamaDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var finalUri = response.RequestMessage?.RequestUri ?? new Uri(OllamaDownloadUrl);
                if (!IsAllowedOllamaDownloadUri(finalUri))
                {
                    return $"Güvenlik nedeniyle indirme reddedildi ({finalUri}); aktapokus-electric kuruldu.";
                }

                var total = response.Content.Headers.ContentLength;
                if (total is > MaxOllamaDownloadBytes)
                {
                    return "Güvenlik sınırı aşıldı (Ollama indirme boyutu çok büyük); aktapokus-electric kuruldu.";
                }

                await using var net = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = File.Create(installerPath);
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await net.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    readTotal += read;
                    if (readTotal > MaxOllamaDownloadBytes)
                    {
                        return "Güvenlik sınırı aşıldı (Ollama indirme boyutu sınırı); aktapokus-electric kuruldu.";
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (total is > 0)
                    {
                        var pct = 78 + (int)(12.0 * readTotal / total.Value);
                        progress?.Report((pct, $"Ollama indiriliyor: {readTotal / (1024 * 1024)} MB / {total.Value / (1024 * 1024)} MB"));
                    }
                }
            }

            progress?.Report((90, "Ollama dijital imzası doğrulanıyor…"));
            var (valid, reason) = AuthenticodeVerifier.VerifySignedBy(installerPath, "Ollama");
            if (!valid)
            {
                return $"Ollama dijital imzası doğrulanamadığı için çalıştırılmadı ({reason}); aktapokus-electric kuruldu.";
            }

            progress?.Report((93, "Ollama kuruluyor…"));
            var startInfo = new ProcessStartInfo(installerPath)
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("/SILENT");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "Ollama kurulumu başlatılamadı (ana uygulama kuruldu).";
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0
                ? "Kuruldu"
                : $"Ollama kurulumu {process.ExitCode} koduyla sonlandı (ana uygulama kuruldu).";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Ollama kurulumu atlandı ({ex.Message}); aktapokus-electric kuruldu.";
        }
        finally
        {
            try
            {
                tempDir.Delete(recursive: true);
            }
            catch
            {
                // Geçici kurulum klasörü silinemezse kuruluma engel olma.
            }
        }
    }

    private static bool IsAllowedOllamaDownloadUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("ollama.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".ollama.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteUninstaller(string installDir, string editorExe, IReadOnlyList<string> installedFiles)
    {
        var uninstallBat = Path.Combine(installDir, "uninstall.cmd");
        var desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "aktapokus-electric.lnk");
        var startLnk = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            "aktapokus-electric.lnk");

        if (HasForbiddenShellChars(installDir)
            || HasForbiddenShellChars(uninstallBat)
            || HasForbiddenShellChars(desktopLnk)
            || HasForbiddenShellChars(startLnk))
        {
            throw new InvalidOperationException("Kaldırma betiği yolu güvensiz karakterler içeriyor.");
        }

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("chcp 65001 >nul");
        script.AppendLine($"del /f /q \"{desktopLnk}\" >nul 2>&1");
        script.AppendLine($"del /f /q \"{startLnk}\" >nul 2>&1");
        script.AppendLine(@"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\aktapokus-electric"" /f >nul 2>&1");

        var fullRoot = TrimTrailingSeparators(Path.GetFullPath(installDir)) + Path.DirectorySeparatorChar;
        var childDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in installedFiles)
        {
            var fullFile = Path.GetFullPath(Path.Combine(installDir, relative));
            if (!fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || HasForbiddenShellChars(fullFile))
            {
                continue;
            }

            script.AppendLine($"del /f /q \"{fullFile}\" >nul 2>&1");
            var dir = Path.GetDirectoryName(fullFile);
            while (!string.IsNullOrEmpty(dir)
                && dir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(TrimTrailingSeparators(dir), installDir, StringComparison.OrdinalIgnoreCase))
            {
                childDirs.Add(TrimTrailingSeparators(dir));
                dir = Path.GetDirectoryName(dir);
            }
        }

        foreach (var subDir in childDirs.OrderByDescending(d => d.Length))
        {
            if (!HasForbiddenShellChars(subDir))
            {
                script.AppendLine($"rmdir \"{subDir}\" >nul 2>&1");
            }
        }

        // Yalnızca kurulumdaki kendi dosyalarını sildikten sonra boşsa klasörü kaldır (rmdir /s kullanılmaz).
        script.AppendLine($"start \"\" /min cmd /c \"timeout /t 2 >nul & del /f /q \\\"{uninstallBat}\\\" >nul 2>&1 & rmdir \\\"{installDir}\\\" >nul 2>&1\"");
        File.WriteAllText(uninstallBat, script.ToString(), new UTF8Encoding(false));

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\aktapokus-electric");
        key.SetValue("DisplayName", "aktapokus-electric");
        key.SetValue("DisplayVersion", "0.1.0");
        key.SetValue("Publisher", "Aktapokus");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", editorExe);
        key.SetValue("UninstallString", $"\"{uninstallBat}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void AddDirectoryToUserPath(string installDir)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        var entries = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Any(e => string.Equals(Path.GetFullPath(e), installDir, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var updated = string.IsNullOrWhiteSpace(current) ? installDir : $"{current.TrimEnd(Path.PathSeparator)}{Path.PathSeparator}{installDir}";
        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.User);
    }

    private static bool HasForbiddenShellChars(string? value) =>
        !string.IsNullOrEmpty(value) && value.IndexOfAny(ForbiddenShellChars) >= 0;

    private static string GetSpecialFolderNormalized(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(path) ? string.Empty : TrimTrailingSeparators(Path.GetFullPath(path));
    }

    private static bool IsSameOrSubpath(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
