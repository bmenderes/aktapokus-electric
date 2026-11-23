using System.Text;
using System.Text.Json;
using Ake.Core;
using Ake.Core.Macros;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>Makro dosyasının şeması (macroVersion 1). Cihaz, bağlantı ve sembol öğeleri proje dosyasındakiyle aynıdır.</summary>
internal sealed record MacroDto(
    int MacroVersion,
    string Name,
    string? Description = null,
    List<DeviceDto>? Devices = null,
    List<ConnectionDto>? Connections = null,
    List<MacroPlacementDto>? Placements = null,
    List<SymbolDto>? Symbols = null,
    List<MacroValueSetDto>? ValueSets = null);

internal sealed record MacroPlacementDto(Guid FunctionId, double X, double Y, int RotationDeg = 0);

internal sealed record MacroValueSetDto(string Name, SortedDictionary<string, string> Values);

/// <summary>
/// Makronun dosyası (<c>*.makro.json</c>, ADR-0025). Makrolar projeler arasında dosya olarak taşınır;
/// okuma projeyle aynı katılıktadır (bilinmeyen alan reddedilir, daha yeni sürüm açıkça söylenir).
/// </summary>
public static class MacroJson
{
    public const int CurrentVersion = 1;

    private static readonly AkeJsonContext Context = new(JsonDefaults.CreateOptions());

    public static string Serialize(Macro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);

        var dto = new MacroDto(
            CurrentVersion,
            macro.Name,
            macro.Description,
            [.. macro.Devices.Select(ProjectMapper.ToDto)],
            [.. macro.Connections.Select(ProjectMapper.ToDto)],
            [.. macro.Placements.Select(p => new MacroPlacementDto(p.FunctionId.Value, p.X, p.Y, p.RotationDeg))],
            [.. macro.Symbols.Select(ProjectMapper.ToDto)],
            macro.ValueSets is { Count: > 0 } sets
                ? [.. sets.Select(s => new MacroValueSetDto(s.Name, new SortedDictionary<string, string>(s.Values.ToDictionary(), StringComparer.Ordinal)))]
                : null);
        return JsonSerializer.Serialize(dto, Context.MacroDto) + "\n";
    }

    public static Result<Macro> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var versioned = JsonDefaults.ReadVersioned(json, "macroVersion", CurrentVersion, "makro", "Makro dosyası");
        if (versioned.IsFailure)
        {
            return Result.Failure<Macro>(versioned.Error!);
        }

        Macro macro;
        try
        {
            var dto = versioned.Value.Root.Deserialize(Context.MacroDto);
            if (dto is null)
            {
                return Invalid("dosya boş.");
            }

            macro = new Macro(
                dto.Name,
                [.. (dto.Devices ?? []).Select(ProjectMapper.ToModel)],
                [.. (dto.Connections ?? []).Select(ProjectMapper.ToModel)],
                [.. (dto.Placements ?? []).Select(p => new MacroPlacement(new FunctionId(p.FunctionId), p.X, p.Y, p.RotationDeg))],
                [.. (dto.Symbols ?? []).Select(ProjectMapper.ToModel)],
                dto.Description,
                dto.ValueSets is { Count: > 0 } sets
                    ? [.. sets.Select(s => new MacroValueSet(s.Name, new Dictionary<string, string>(s.Values, StringComparer.Ordinal)))]
                    : null);
        }
        catch (JsonException ex)
        {
            return Invalid(ex.Message);
        }

        return macro.Check() is { } error ? Result.Failure<Macro>(error) : Result.Success(macro);
    }

    public static Result<Macro> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Macro>(new Error("makro.dosya_okunamadi", $"'{path}' okunamadı: {ex.Message}"));
        }
    }

    public static Result Save(Macro macro, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            File.WriteAllText(path, Serialize(macro), new UTF8Encoding(false));
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("makro.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }

    private static Result<Macro> Invalid(string detail) =>
        Result.Failure<Macro>(new Error("makro.json_gecersiz", $"Makro dosyası okunamadı: {detail}"));
}
