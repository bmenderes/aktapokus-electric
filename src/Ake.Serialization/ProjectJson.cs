using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>Proje modelini canonical JSON metnine çevirir ve geri okur.</summary>
public static class ProjectJson
{
    public const int CurrentSchemaVersion = 6;

    // Migrations[i], şema (i + 1) dosyasını şema (i + 2) biçimine çevirir.
    private static readonly Action<JsonObject>[] Migrations =
    [
        MigrateV1ToV2,
        MigrateV2ToV3,
        MigrateV3ToV4,
        MigrateV4ToV5,
        MigrateV5ToV6,
    ];

    private static readonly AkeJsonContext Context = new(JsonDefaults.CreateOptions());

    /// <summary>
    /// Aynı model her zaman aynı metni üretir: alan sırası sabit, liste sırası modeldeki sıra,
    /// satır sonu LF. Metin sonunda tek satır sonu vardır.
    /// </summary>
    public static string Serialize(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return JsonSerializer.Serialize(ProjectMapper.ToDto(project), Context.ProjectDto) + "\n";
    }

    public static Result<Project> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // Şema versiyonu asıl okumadan önce denetlenir; yoksa daha yeni bir dosya "bilinmeyen alan"
        // hatasıyla reddedilir ve kullanıcı gerçek nedeni göremez.
        var versioned = JsonDefaults.ReadVersioned(json, "schemaVersion", CurrentSchemaVersion, "proje", "Proje dosyası");
        if (versioned.IsFailure)
        {
            return Result.Failure<Project>(versioned.Error!);
        }

        var (root, version) = versioned.Value;
        for (var from = version; from < CurrentSchemaVersion; from++)
        {
            Migrations[from - 1](root);
        }

        root["schemaVersion"] = CurrentSchemaVersion;

        ProjectDto? dto;
        try
        {
            dto = root.Deserialize(Context.ProjectDto);
        }
        catch (JsonException ex)
        {
            return Result.Failure<Project>(new Error("proje.json_gecersiz", $"Proje dosyası okunamadı: {ex.Message}"));
        }

        return dto is null
            ? Result.Failure<Project>(new Error("proje.json_gecersiz", "Proje dosyası boş."))
            : ProjectMapper.ToModel(dto);
    }

    // Şema 2, function'a isteğe bağlı ratedCurrentA alanını ekledi. Eski dosyada alan yoktur ve
    // yokluğu geçerlidir; dönüştürülecek veri yok.
    private static void MigrateV1ToV2(JsonObject root)
    {
    }

    // Şema 3, proje alanlarını (properties), sayfa şablonlarını (sheetTemplates) ve sayfanın şablon
    // atıfını (templateId) ekledi. Hepsi isteğe bağlıdır; eski dosyada yoklukları geçerlidir.
    private static void MigrateV2ToV3(JsonObject root)
    {
    }

    // Şema 4, kullanıcı sembollerini (symbols) ve function'ın sembol atfını (symbolId) ekledi. İkisi de
    // isteğe bağlıdır; eski dosyada yoklukları geçerlidir.
    private static void MigrateV3ToV4(JsonObject root)
    {
    }

    // Şema 5, projenin tag şemasını (tagScheme) ekledi. İsteğe bağlıdır; yokluğu "harf + sayı" demektir.
    private static void MigrateV4ToV5(JsonObject root)
    {
    }

    // Şema 6, sayfalara serbest grafik (graphics) ekledi. İsteğe bağlıdır; eski dosyada yokluğu "grafik yok" demektir.
    private static void MigrateV5ToV6(JsonObject root)
    {
    }
}

internal static class JsonDefaults
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowOutOfOrderMetadataProperties = true,
    };

    /// <summary>Metni JSON nesnesi olarak okur ve sürüm alanını denetler.</summary>
    public static Result<(JsonObject Root, int Version)> ReadVersioned(
        string json, string versionProperty, int currentVersion, string codePrefix, string what)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result.Failure<(JsonObject, int)>(
                new Error($"{codePrefix}.json_gecersiz", $"{what} geçerli JSON değil: {ex.Message}"));
        }

        if (node is not JsonObject root
            || root[versionProperty] is not JsonValue value
            || value.GetValueKind() != JsonValueKind.Number
            || !value.TryGetValue<int>(out var version)
            || version < 1)
        {
            return Result.Failure<(JsonObject, int)>(
                new Error($"{codePrefix}.sema_yok", $"{what} içinde geçerli bir {versionProperty} yok."));
        }

        return version > currentVersion
            ? Result.Failure<(JsonObject, int)>(new Error(
                $"{codePrefix}.sema_yeni",
                $"{what} daha yeni bir sürümle kaydedilmiş ({versionProperty} {version}, desteklenen {currentVersion})."))
            : Result.Success((root, version));
    }
}

[JsonSerializable(typeof(ProjectDto))]
[JsonSerializable(typeof(PatchDto))]
[JsonSerializable(typeof(SheetTemplateDto))]
[JsonSerializable(typeof(SymbolDto))]
[JsonSerializable(typeof(MacroDto))]
[JsonSerializable(typeof(AuditEntryDto))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal sealed partial class AkeJsonContext : JsonSerializerContext;
