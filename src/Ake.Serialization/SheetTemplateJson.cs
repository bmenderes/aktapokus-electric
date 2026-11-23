using System.Text;
using System.Text.Json;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>
/// Sayfa şablonunun tek başına dosyası. Biçimi, proje dosyasındaki <c>sheetTemplates</c> öğesiyle
/// aynıdır; böylece bir şablon projeler arasında dosya olarak taşınabilir.
/// </summary>
public static class SheetTemplateJson
{
    private static readonly AkeJsonContext Context = new(JsonDefaults.CreateOptions());

    public static string Serialize(SheetTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return JsonSerializer.Serialize(ProjectMapper.ToDto(template), Context.SheetTemplateDto) + "\n";
    }

    public static Result<SheetTemplate> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        SheetTemplate template;
        try
        {
            var dto = JsonSerializer.Deserialize(json, Context.SheetTemplateDto);
            if (dto is null)
            {
                return Invalid("dosya boş.");
            }

            template = ProjectMapper.ToModel(dto);
        }
        catch (JsonException ex)
        {
            return Invalid(ex.Message);
        }

        return template.Check() is { } error ? Result.Failure<SheetTemplate>(error) : Result.Success(template);
    }

    public static Result<SheetTemplate> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<SheetTemplate>(
                new Error("sablon.dosya_okunamadi", $"'{path}' okunamadı: {ex.Message}"));
        }
    }

    public static Result Save(SheetTemplate template, string path)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(path);

        if (template.Check() is { } invalid)
        {
            return Result.Failure(invalid);
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, Serialize(template), Encoding.UTF8);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(
                new Error("sablon.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }

    private static Result<SheetTemplate> Invalid(string detail) =>
        Result.Failure<SheetTemplate>(new Error("sablon.json_gecersiz", $"Sayfa şablonu dosyası okunamadı: {detail}"));
}
