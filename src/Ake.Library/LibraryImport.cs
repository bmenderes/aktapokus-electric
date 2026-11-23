using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Model;

namespace Ake.Library;

/// <summary>
/// Kullanıcının kendi dosyasından ürün okur. Uygulama üretici verisi dağıtmaz; her kayıt kaynağını ve
/// lisansını taşır. JSON'da bunlar dosya düzeyinde verilir, ürün düzeyinde ezilebilir.
/// </summary>
public static class LibraryImport
{
    public const int CurrentImportVersion = 1;

    private static readonly string[] KnownCsvColumns =
        ["manufacturer", "orderCode", "typeDesignation", "description", "category"];

    private static readonly LibraryJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    });

    public static Result<IReadOnlyList<LibraryProduct>> FromFile(
        string path, string? source = null, string? license = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var effectiveSource = source ?? string.Empty;
        var effectiveLicense = license ?? string.Empty;

        switch (extension)
        {
            case ".bmecat":
                return BmecatImporter.FromFile(path, effectiveSource, effectiveLicense);
            case ".gsdml":
                return GsdmlImporter.FromFile(path, effectiveSource, effectiveLicense);
            case ".eds":
                return EdsImporter.FromFile(path, effectiveSource, effectiveLicense);
            case ".iodd":
                return IoddImporter.FromFile(path, effectiveSource, effectiveLicense);
            case ".aasx":
                return AasxImporter.FromFile(path, effectiveSource, effectiveLicense);
            case ".edz":
                return EdzImporter.FromFile(path, effectiveSource, effectiveLicense);
            case ".skycad":
                return SkyCadImporter.FromFile(path, effectiveSource, effectiveLicense);
            case ".xml":
                return FromXmlAuto(path, effectiveSource, effectiveLicense);
        }

        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("kutuphane.dosya_okunamadi", $"'{path}' okunamadı: {ex.Message}");
        }

        return extension switch
        {
            ".json" => FromJson(text, source, license),
            ".csv" => FromCsv(text, source, license),
            _ => Fail(
                "kutuphane.bicim_yok",
                $"Bilinmeyen dosya türü '{extension}'. Türler: .json, .csv, .bmecat, .gsdml, .eds, .iodd, .aasx, .edz, .skycad, .xml"),
        };
    }

    private static Result<IReadOnlyList<LibraryProduct>> FromXmlAuto(string path, string source, string license)
    {
        var read = ManufacturerImportSupport.ReadTextFile(path);
        if (read.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(read.Error!);
        }

        var loaded = ManufacturerImportSupport.LoadXml(read.Value, "Üretici XML");
        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(loaded.Error!);
        }

        var root = loaded.Value.Root?.Name.LocalName.ToLowerInvariant() ?? string.Empty;
        if (root.Contains("bmecat"))
        {
            return BmecatImporter.FromXml(read.Value, source, license);
        }

        if (root.Contains("iso15745"))
        {
            return GsdmlImporter.FromXml(read.Value, source, license);
        }

        if (root.Contains("iodevice"))
        {
            return IoddImporter.FromXml(read.Value, source, license);
        }

        if (root.Contains("aas") || root == "environment")
        {
            return AasxImporter.FromXml(read.Value, source, license);
        }

        if (root == "part" || loaded.Value.ElementsNamed("part").Any())
        {
            return EdzImporter.FromXml(read.Value, source, license);
        }

        if (root.Contains("skycad") || loaded.Value.ElementsNamed("CataloguePart").Any())
        {
            return SkyCadImporter.FromXml(read.Value, source, license);
        }

        return Fail("kutuphane.bicim_yok", $"XML kök öğesi '{loaded.Value.Root?.Name.LocalName}' tanınmadı.");
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromJson(
        string json, string? source = null, string? license = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        ImportFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize(json, Context.ImportFileDto);
        }
        catch (JsonException ex)
        {
            return Fail("kutuphane.json_gecersiz", $"Ürün dosyası okunamadı: {ex.Message}");
        }

        if (file is null || file.LibraryImportVersion != CurrentImportVersion)
        {
            return Fail(
                "kutuphane.surum_gecersiz",
                $"Ürün dosyasının libraryImportVersion değeri {CurrentImportVersion} olmalı.");
        }

        return Result.Success<IReadOnlyList<LibraryProduct>>(
        [
            .. file.Products.Select(product => new LibraryProduct(
                new ProductDefinition(
                    product.Manufacturer,
                    product.OrderCode,
                    [
                        .. (product.Functions ?? []).Select(function => new FunctionTemplate(
                            function.Kind,
                            function.ConnectionPoints ?? [],
                            function.PassThrough,
                            function.Description,
                            function.RatedCurrentA)),
                    ],
                    product.TypeDesignation,
                    product.Description,
                    product.Mounting is { } m
                        ? new Mounting(m.Type, m.WidthMm, m.HeightMm, m.DepthMm, m.ClearanceMm, m.HeatLossW, m.StepRef)
                        : null),
                product.Source ?? file.Source ?? source ?? string.Empty,
                product.License ?? file.License ?? license ?? string.Empty,
                product.Category,
                [.. (product.Properties ?? []).Select(p => new ProductProperty(p.Key, p.Value, p.Unit))])),
        ]);
    }

    /// <summary>
    /// Toplu ürün listesi. Başlık satırı zorunludur; <c>manufacturer</c> ve <c>orderCode</c> sütunları
    /// gerekir. Bilinmeyen her sütun ürün özelliği olur. Function şablonu CSV ile verilemez.
    /// </summary>
    public static Result<IReadOnlyList<LibraryProduct>> FromCsv(
        string csv, string? source, string? license, char separator = ',')
    {
        ArgumentNullException.ThrowIfNull(csv);

        var rows = ParseCsv(csv, separator);
        if (rows.Count == 0)
        {
            return Fail("kutuphane.csv_gecersiz", "CSV dosyası boş.");
        }

        var header = rows[0].Select(cell => cell.Trim()).ToList();
        var manufacturerAt = header.IndexOf("manufacturer");
        var orderCodeAt = header.IndexOf("orderCode");
        if (manufacturerAt < 0 || orderCodeAt < 0)
        {
            return Fail("kutuphane.csv_gecersiz", "CSV başlığında 'manufacturer' ve 'orderCode' sütunları gerekli.");
        }

        var products = new List<LibraryProduct>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count != header.Count)
            {
                return Fail(
                    "kutuphane.csv_gecersiz",
                    $"Satır {r + 1}: {header.Count} sütun bekleniyordu, {row.Count} bulundu.");
            }

            string? Cell(string column)
            {
                var index = header.IndexOf(column);
                return index >= 0 && row[index].Length > 0 ? row[index] : null;
            }

            var properties = header
                .Select((name, index) => (name, value: row[index]))
                .Where(item => !KnownCsvColumns.Contains(item.name) && item.value.Length > 0)
                .Select(item => new ProductProperty(item.name, item.value))
                .ToList();

            products.Add(new LibraryProduct(
                new ProductDefinition(
                    row[manufacturerAt], row[orderCodeAt], [], Cell("typeDesignation"), Cell("description")),
                source ?? string.Empty,
                license ?? string.Empty,
                Cell("category"),
                properties));
        }

        return Result.Success<IReadOnlyList<LibraryProduct>>(products);
    }

    private static List<List<string>> ParseCsv(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        void EndCell()
        {
            row.Add(cell.ToString());
            cell.Clear();
        }

        void EndRow()
        {
            EndCell();
            if (row.Count > 1 || row[0].Length > 0)
            {
                rows.Add(row);
            }

            row = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i == 0 && c == '﻿')
            {
                continue;
            }

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                EndCell();
            }
            else if (c == '\n')
            {
                EndRow();
            }
            else if (c != '\r')
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            EndRow();
        }

        return rows;
    }

    private static Result<IReadOnlyList<LibraryProduct>> Fail(string code, string message) =>
        Result.Failure<IReadOnlyList<LibraryProduct>>(new Error(code, message));
}

internal sealed record ImportFileDto(
    int LibraryImportVersion,
    List<ImportProductDto> Products,
    string? Source = null,
    string? License = null);

internal sealed record ImportProductDto(
    string Manufacturer,
    string OrderCode,
    string? TypeDesignation = null,
    string? Description = null,
    string? Category = null,
    ImportMountingDto? Mounting = null,
    List<ImportPropertyDto>? Properties = null,
    List<ImportFunctionDto>? Functions = null,
    string? Source = null,
    string? License = null);

internal sealed record ImportMountingDto(
    MountingType Type,
    double WidthMm,
    double HeightMm,
    double DepthMm,
    double ClearanceMm = 0,
    double HeatLossW = 0,
    string? StepRef = null);

internal sealed record ImportPropertyDto(string Key, string Value, string? Unit = null);

internal sealed record ImportFunctionDto(
    FunctionKind Kind,
    List<string>? ConnectionPoints = null,
    bool PassThrough = false,
    string? Description = null,
    decimal? RatedCurrentA = null);

[JsonSerializable(typeof(ImportFileDto))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal sealed partial class LibraryJsonContext : JsonSerializerContext;
