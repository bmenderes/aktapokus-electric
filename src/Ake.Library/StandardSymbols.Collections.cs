using Ake.Core;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Library;

/// <summary>Çalışma alanında keşfedilen yerel sembol kütüphanesi kaynağı.</summary>
public sealed record DiscoveredSymbolCollection(string Name, string Path, string License, string Kind);

/// <summary>Klasör veya dosyadan toplu sembol içe aktarma sonucu.</summary>
public sealed record SymbolCollectionImportResult(int Added, int Updated, int Skipped);

public static partial class StandardSymbols
{
    public const string FavreLicense = "CC BY 4.0, B. Favre (fav0001_dxf_symbol)";

    private static readonly string[] IgnoredFrameSegments =
    [
        "fav0090_drawing_frames_iso216",
        "fav0095_title_bloc_iso7200",
    ];

    /// <summary>
    /// Çalışma alanı veya uygulama kökünde bulunan yerel sembol koleksiyonlarını keşfeder:
    /// örnek QElectroTech seti, B. Favre IEC 60617 DXF kütüphanesi ve QElectroTech elektrik koleksiyonu.
    /// </summary>
    public static IReadOnlyList<DiscoveredSymbolCollection> DiscoverCollections(string? baseDirectory = null)
    {
        var roots = CandidateRoots(baseDirectory);
        var discovered = new List<DiscoveredSymbolCollection>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string fullPath, string license, string kind)
        {
            var normalized = Path.GetFullPath(fullPath);
            if (Directory.Exists(normalized) && seenPaths.Add(normalized))
            {
                discovered.Add(new DiscoveredSymbolCollection(name, normalized, license, kind));
            }
        }

        foreach (var root in roots)
        {
            var exampleQet = Path.Combine(root, "examples", "qelectrotech");
            if (Directory.Exists(exampleQet))
            {
                Add("Örnek QElectroTech Seti", exampleQet, "Apache-2.0 (aktapokus-electric örnek seti)", "qet");
            }

            var favreRoot = Path.Combine(root, "docs", "fav0001_dxf_symbol", "fav0001_dxf_library");
            if (Directory.Exists(favreRoot))
            {
                var blocLib = Path.Combine(favreRoot, "fav0090_dxf_bloc_library");
                if (Directory.Exists(blocLib))
                {
                    foreach (var sub in Directory.EnumerateDirectories(blocLib).Order(StringComparer.OrdinalIgnoreCase))
                    {
                        var folderName = Path.GetFileName(sub);
                        if (IgnoredFrameSegments.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        Add($"Favre DXF · {folderName}", sub, FavreLicense, "dxf");
                    }
                }

                string[] extraFavreDirs =
                [
                    "fav0150_dxf_bloc_pneumatic",
                    "fav0200_dxf_diagram_connection",
                    "fav0300_dxf_single_line_diagram",
                    "fav0400_dxf_plc_starter_converter",
                ];
                foreach (var dirName in extraFavreDirs)
                {
                    var dirPath = Path.Combine(favreRoot, dirName);
                    if (Directory.Exists(dirPath))
                    {
                        Add($"Favre DXF · {dirName}", dirPath, FavreLicense, "dxf");
                    }
                }
            }

            var qetElectric = Path.Combine(root, "Ornek", "qelectrotech-elements", "10_electric");
            if (Directory.Exists(qetElectric))
            {
                Add("QElectroTech Elektrik Koleksiyonu", qetElectric, QElectroTechImport.CollectionLicenseName, "qet");
            }
        }

        return discovered;
    }

    /// <summary>
    /// Verilen dosya (.elmt, .dxf, .dwg, .sembol.json) ya da klasördeki tüm geçerli sembolleri
    /// <paramref name="libraryPath"/> içindeki paylaşılan kütüphaneye aktarır.
    /// </summary>
    public static Result<SymbolCollectionImportResult> ImportFromPath(
        string libraryPath,
        string sourcePath,
        string? license = null,
        double? scale = null,
        bool blocks = false)
    {
        ArgumentNullException.ThrowIfNull(libraryPath);
        ArgumentNullException.ThrowIfNull(sourcePath);

        if (scale is not null && (!(scale.Value > 0) || !double.IsFinite(scale.Value)))
        {
            return Result.Failure<SymbolCollectionImportResult>(
                new Error("sembol.olcek", $"Ölçek sıfırdan büyük olmalı: {scale}"));
        }

        var fullSource = Path.GetFullPath(sourcePath);
        var isFile = File.Exists(fullSource);
        var isDir = Directory.Exists(fullSource);
        if (!isFile && !isDir)
        {
            return Result.Failure<SymbolCollectionImportResult>(
                new Error("kutuphane.yol_yok", $"Kaynak bulunamadı: {sourcePath}"));
        }

        var effectiveLicense = license ?? InferDefaultLicense(fullSource);
        var validById = new Dictionary<SymbolId, LibrarySymbol>();
        var skipped = 0;

        if (isFile)
        {
            if (fullSource.EndsWith(".sembol.json", StringComparison.OrdinalIgnoreCase))
            {
                var loaded = SymbolJson.Load(fullSource);
                if (loaded.IsFailure)
                {
                    return Result.Failure<SymbolCollectionImportResult>(loaded.Error!);
                }

                var def = string.IsNullOrWhiteSpace(loaded.Value.License) && !string.IsNullOrWhiteSpace(effectiveLicense)
                    ? loaded.Value with { License = effectiveLicense }
                    : loaded.Value;
                if (def.Check() is { } invalid)
                {
                    return Result.Failure<SymbolCollectionImportResult>(invalid);
                }

                if (string.IsNullOrWhiteSpace(def.License))
                {
                    return Result.Failure<SymbolCollectionImportResult>(new Error(
                        "kutuphane.kaynak_yok", $"'{def.Name}' sembolünün lisansı yok."));
                }

                var folderCategory = Path.GetFileName(Path.GetDirectoryName(fullSource));
                validById[def.Id] = new LibrarySymbol(def, NormalizeCategoryPath(folderCategory));
            }
            else if (DxfImport.IsCadFile(fullSource))
            {
                skipped += CollectImported(
                    DxfImport.FromPath(fullSource, effectiveLicense, scale ?? 1, blocks),
                    rootFolderName: Path.GetFileName(Path.GetDirectoryName(fullSource)),
                    validById);
            }
            else if (fullSource.EndsWith(".elmt", StringComparison.OrdinalIgnoreCase))
            {
                skipped += CollectImported(
                    QElectroTechImport.FromPath(fullSource, effectiveLicense, scale ?? QElectroTechImport.DefaultScaleMmPerPixel),
                    rootFolderName: Path.GetFileName(Path.GetDirectoryName(fullSource)),
                    validById);
            }
            else
            {
                return Result.Failure<SymbolCollectionImportResult>(new Error(
                    "kutuphane.bicim_yok", $"Desteklenmeyen sembol dosyası: '{Path.GetFileName(fullSource)}' (.elmt, .dxf, .dwg veya .sembol.json bekleniyordu)."));
            }
        }
        else
        {
            var rootName = Path.GetFileName(fullSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var qetResults = QElectroTechImport.FromPath(
                fullSource,
                effectiveLicense,
                scale ?? QElectroTechImport.DefaultScaleMmPerPixel);
            skipped += CollectImported(qetResults, rootName, validById);

            if (Directory.EnumerateFiles(fullSource, "*", SearchOption.AllDirectories).Any(DxfImport.IsCadFile))
            {
                var dxfResults = DxfImport.FromPath(fullSource, effectiveLicense, scale ?? 1, blocks)
                    .Where(item => !IsIgnoredFramePath(item.File));
                skipped += CollectImported(dxfResults, rootName, validById);
            }

            foreach (var jsonFile in Directory.EnumerateFiles(fullSource, "*.sembol.json", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                var loaded = SymbolJson.Load(jsonFile);
                if (loaded.IsFailure)
                {
                    skipped++;
                    continue;
                }

                var def = string.IsNullOrWhiteSpace(loaded.Value.License) && !string.IsNullOrWhiteSpace(effectiveLicense)
                    ? loaded.Value with { License = effectiveLicense }
                    : loaded.Value;
                if (def.Check() is not null || string.IsNullOrWhiteSpace(def.License))
                {
                    skipped++;
                    continue;
                }

                var rel = Path.GetRelativePath(fullSource, jsonFile);
                var category = CategoryFromRelativeFile(rootName, rel);
                validById[def.Id] = new LibrarySymbol(def, category);
            }
        }

        if (validById.Count == 0)
        {
            return Result.Success(new SymbolCollectionImportResult(0, 0, skipped));
        }

        var opened = ComponentLibrary.Open(libraryPath);
        if (opened.IsFailure)
        {
            return Result.Failure<SymbolCollectionImportResult>(opened.Error!);
        }

        using var library = opened.Value;
        var imported = library.ImportSymbols(validById.Values);
        return imported.IsFailure
            ? Result.Failure<SymbolCollectionImportResult>(imported.Error!)
            : Result.Success(new SymbolCollectionImportResult(imported.Value.Added, imported.Value.Updated, skipped));
    }

    /// <summary>
    /// <see cref="DiscoverCollections"/> ile çalışma alanında bulunan tüm sembol kütüphanelerini
    /// <paramref name="libraryPath"/> içine aktarır.
    /// </summary>
    public static Result<SymbolCollectionImportResult> ImportDiscoveredCollections(
        string libraryPath,
        string? baseDirectory = null,
        bool includeQetCollection = true)
    {
        ArgumentNullException.ThrowIfNull(libraryPath);

        var ensure = EnsureInstalled(libraryPath);
        if (ensure.IsFailure)
        {
            return Result.Failure<SymbolCollectionImportResult>(ensure.Error!);
        }

        var collections = DiscoverCollections(baseDirectory)
            .Where(item => includeQetCollection || !item.Path.Contains("qelectrotech-elements", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalAdded = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;

        foreach (var collection in collections)
        {
            var result = ImportFromPath(libraryPath, collection.Path, collection.License);
            if (result.IsFailure)
            {
                return result;
            }

            totalAdded += result.Value.Added;
            totalUpdated += result.Value.Updated;
            totalSkipped += result.Value.Skipped;
        }

        return Result.Success(new SymbolCollectionImportResult(totalAdded, totalUpdated, totalSkipped));
    }

    private static int CollectImported(
        IEnumerable<ImportedSymbol> items,
        string? rootFolderName,
        Dictionary<SymbolId, LibrarySymbol> target)
    {
        var skipped = 0;
        foreach (var item in items)
        {
            if (item.Symbol is not { } symbol || symbol.Check() is not null || string.IsNullOrWhiteSpace(symbol.License))
            {
                skipped++;
                continue;
            }

            var category = CategoryFromRelativeFile(rootFolderName, item.File);
            target[symbol.Id] = new LibrarySymbol(symbol, category);
        }

        return skipped;
    }

    private static string? CategoryFromRelativeFile(string? rootFolderName, string relativeFile)
    {
        var cleanRel = relativeFile.Replace('\\', '/');
        var fileOnly = cleanRel.IndexOf('#') is var hash and >= 0 ? cleanRel[..hash] : cleanRel;
        var dir = Path.GetDirectoryName(fileOnly)?.Replace('\\', '/');
        var combined = string.IsNullOrWhiteSpace(dir)
            ? rootFolderName
            : (string.IsNullOrWhiteSpace(rootFolderName) ? dir : $"{rootFolderName}/{dir}");
        return NormalizeCategoryPath(combined);
    }

    private static string? NormalizeCategoryPath(string? category) =>
        string.IsNullOrWhiteSpace(category) ? null : category.Replace('\\', '/').Trim('/');

    private static bool IsIgnoredFramePath(string relativeFile)
    {
        var normalized = relativeFile.Replace('\\', '/');
        return IgnoredFrameSegments.Any(seg => normalized.Contains(seg, StringComparison.OrdinalIgnoreCase));
    }

    private static string? InferDefaultLicense(string fullPath)
    {
        if (fullPath.Contains("fav0001_dxf_symbol", StringComparison.OrdinalIgnoreCase))
        {
            return FavreLicense;
        }

        if (fullPath.Contains(Path.Combine("examples", "qelectrotech"), StringComparison.OrdinalIgnoreCase))
        {
            return "Apache-2.0 (aktapokus-electric örnek seti)";
        }

        return null;
    }

    private static IReadOnlyList<string> CandidateRoots(string? baseDirectory)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(string? start)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                return;
            }

            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                if (seen.Add(current.FullName))
                {
                    roots.Add(current.FullName);
                }

                var nestedRepo = Path.Combine(current.FullName, "aktapokus-electric");
                if (Directory.Exists(nestedRepo) && seen.Add(nestedRepo))
                {
                    roots.Add(nestedRepo);
                }

                current = current.Parent;
            }
        }

        Walk(baseDirectory);
        Walk(Directory.GetCurrentDirectory());
        Walk(AppContext.BaseDirectory);
        return roots;
    }
}
